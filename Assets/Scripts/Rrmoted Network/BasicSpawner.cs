using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

public class BasicSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    private enum LocalRole
    {
        None,
        ActorHost,
        AudienceClient
    }

    [Header("Auto Start")]
    [SerializeField] private bool _autoStart = false;
    [SerializeField] private bool _autoStartAsActorHost = true;

    [Header("Client Retry")]
    [Tooltip("Audience Client 找不到 Host 房间时是否自动重试。")]
    [SerializeField] private bool _retryClientJoin = true;

    [Tooltip("Audience Client 每次重试加入房间的间隔秒数。")]
    [SerializeField] private float _clientRetryDelay = 3f;

    [Tooltip("最大重试次数。设置为 0 表示无限重试。")]
    [SerializeField] private int _maxClientRetryCount = 0;

    [Header("Network Prefabs")]
    [SerializeField] private NetworkPrefabRef _actorAvatarPrefab;

    [Header("WebRTC Signal Hub")]
    [SerializeField] private NetworkPrefabRef _webRtcSignalHubPrefab;

    [Header("Webcam Control Hub")]
    [SerializeField] private NetworkPrefabRef _networkWebcamControlHubPrefab;

    [Header("Spawn Points")]
    [SerializeField] private Transform _actorSpawnPoint;

    [Header("Local Systems")]
    [Tooltip("演员端本地系统。Quest / OVR / mocap / face / eye / hand tracking rig。")]
    [SerializeField] private GameObject _actorLocalRig;

    [Tooltip("观众端本地 Fish Tank 系统。拖入 AudienceFishTankRig 根物体。")]
    [SerializeField] private GameObject _audienceFishTankRig;

    [Header("Webcam Role Objects")]
    [Tooltip("演员端控制菜单。包含 CameraDropdown / StartButton / StopButton。")]
    [SerializeField] private GameObject _performerMenu;

    [Tooltip("观众端本地摄像头管理器。实际运行在 Audience Client。")]
    [SerializeField] private GameObject _webcamManager;

    [Tooltip("观众端 WebRTC 发送端。实际运行在 Audience Client。")]
    [SerializeField] private GameObject _audienceWebRtcSender;

    [Tooltip("观众端 webcam runtime。接收 Performer 命令后启动本地 webcam。")]
    [SerializeField] private GameObject _audienceWebcamRuntime;

    [Tooltip("演员端 WebRTC 接收端。实际运行在 Actor Host。")]
    [SerializeField] private GameObject _actorWebRtcReceiver;

    [Tooltip("演员端唯一 webcam 显示屏。显示观众端摄像头画面。")]
    [SerializeField] private GameObject _webcamScreen;

    [Header("Actor Local Sources")]
    [Tooltip("演员头部追踪源。后续可绑定 CenterEyeAnchor。")]
    [SerializeField] private Transform _actorHeadSource;

    [Tooltip("演员左手追踪源。后续可绑定 LeftHandAnchor。")]
    [SerializeField] private Transform _actorLeftHandSource;

    [Tooltip("演员右手追踪源。后续可绑定 RightHandAnchor。")]
    [SerializeField] private Transform _actorRightHandSource;

    [Header("Session Settings")]
    [SerializeField] private string _sessionName = "TestRoom";

    [Tooltip("如果两个 Build Profile 都只放一个场景，建议保持为 0。")]
    [SerializeField] private int _sceneBuildIndex = 0;

    private NetworkRunner _runner;

    private NetworkObject _webRtcSignalHubObject;
    private NetworkObject _networkWebcamControlHubObject;
    private NetworkObject _actorAvatarObject;

    private LocalRole _localRole = LocalRole.None;

    private int _clientRetryCount = 0;
    private bool _isStartingGame = false;

    private readonly Dictionary<PlayerRef, NetworkObject> _spawnedObjects =
        new Dictionary<PlayerRef, NetworkObject>();

    private void Start()
    {
        if (!_autoStart)
            return;

        if (_autoStartAsActorHost)
        {
            StartGame(GameMode.Host, LocalRole.ActorHost);
        }
        else
        {
            StartGame(GameMode.Client, LocalRole.AudienceClient);
        }
    }

    private async void StartGame(GameMode mode, LocalRole role)
    {
        if (_isStartingGame)
        {
            Debug.LogWarning("BasicSpawner: StartGame is already running. Ignored.");
            return;
        }

        if (_runner != null)
        {
            Debug.LogWarning("BasicSpawner: NetworkRunner already exists. StartGame ignored.");
            return;
        }

        _isStartingGame = true;

        _localRole = role;
        ApplyLocalRole();

        _runner = GetComponent<NetworkRunner>();

        if (_runner == null)
        {
            _runner = gameObject.AddComponent<NetworkRunner>();
        }

        _runner.ProvideInput = true;
        _runner.AddCallbacks(this);

        SceneRef scene = SceneRef.FromIndex(_sceneBuildIndex);

        Debug.Log(
            $"BasicSpawner: Starting Fusion. " +
            $"Mode: {mode}, Role: {_localRole}, Session: {_sessionName}, SceneIndex: {_sceneBuildIndex}"
        );

        StartGameResult result = await _runner.StartGame(new StartGameArgs()
        {
            GameMode = mode,
            SessionName = _sessionName,
            Scene = scene,
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        });

        _isStartingGame = false;

        if (result.Ok)
        {
            _clientRetryCount = 0;
            Debug.Log("BasicSpawner: Fusion StartGame succeeded.");
        }
        else
        {
            Debug.LogError($"BasicSpawner: Fusion StartGame failed. Reason: {result.ShutdownReason}");

            bool shouldRetry =
                _retryClientJoin &&
                mode == GameMode.Client &&
                result.ShutdownReason == ShutdownReason.GameNotFound &&
                CanRetryClientJoin();

            if (shouldRetry)
            {
                _clientRetryCount++;

                Debug.LogWarning(
                    $"BasicSpawner: Game not found. " +
                    $"Retrying audience client join in {_clientRetryDelay} seconds. " +
                    $"Retry count: {_clientRetryCount}"
                );

                Invoke(nameof(RetryJoinAsAudience), _clientRetryDelay);
            }
            else
            {
                Debug.LogError("BasicSpawner: StartGame failed and will not retry.");
            }
        }
    }

    private bool CanRetryClientJoin()
    {
        if (_maxClientRetryCount <= 0)
            return true;

        return _clientRetryCount < _maxClientRetryCount;
    }

    private void RetryJoinAsAudience()
    {
        Debug.Log("BasicSpawner: Retrying join as Audience Client...");

        CleanupRunner();

        StartGame(GameMode.Client, LocalRole.AudienceClient);
    }

    private void CleanupRunner()
    {
        if (_runner == null)
            return;

        try
        {
            _runner.RemoveCallbacks(this);

            NetworkSceneManagerDefault sceneManager =
                _runner.GetComponent<NetworkSceneManagerDefault>();

            if (sceneManager != null)
            {
                Destroy(sceneManager);
            }

            Destroy(_runner);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("BasicSpawner: Runner cleanup exception: " + exception.Message);
        }

        _runner = null;
        _webRtcSignalHubObject = null;
        _networkWebcamControlHubObject = null;
        _actorAvatarObject = null;
        _spawnedObjects.Clear();
    }

    private void ApplyLocalRole()
    {
        bool isActor = _localRole == LocalRole.ActorHost;
        bool isAudience = _localRole == LocalRole.AudienceClient;

        if (_actorLocalRig != null)
        {
            _actorLocalRig.SetActive(isActor);
        }

        if (_audienceFishTankRig != null)
        {
            _audienceFishTankRig.SetActive(isAudience);
        }

        if (_performerMenu != null)
        {
            _performerMenu.SetActive(isActor);
        }

        if (_actorWebRtcReceiver != null)
        {
            _actorWebRtcReceiver.SetActive(isActor);
        }

        if (_webcamScreen != null)
        {
            _webcamScreen.SetActive(isActor);
        }

        if (_webcamManager != null)
        {
            _webcamManager.SetActive(isAudience);
        }

        if (_audienceWebRtcSender != null)
        {
            _audienceWebRtcSender.SetActive(isAudience);
        }

        if (_audienceWebcamRuntime != null)
        {
            _audienceWebcamRuntime.SetActive(isAudience);
        }

        Debug.Log($"BasicSpawner: Local role applied: {_localRole}");
    }

    private void OnGUI()
    {
        if (_runner != null || _isStartingGame)
            return;

        if (GUI.Button(new Rect(0, 0, 260, 45), "Start as Actor Host"))
        {
            StartGame(GameMode.Host, LocalRole.ActorHost);
        }

        if (GUI.Button(new Rect(0, 50, 260, 45), "Join as Audience"))
        {
            StartGame(GameMode.Client, LocalRole.AudienceClient);
        }
    }

    void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log(
            $"BasicSpawner: Player joined: {player}. " +
            $"IsServer: {runner.IsServer}, LocalPlayer: {runner.LocalPlayer}"
        );

        if (!runner.IsServer)
            return;

        SpawnWebRtcSignalHubIfNeeded(runner);
        SpawnNetworkWebcamControlHubIfNeeded(runner);

        bool isActorHostPlayer = player == runner.LocalPlayer;

        if (isActorHostPlayer)
        {
            SpawnActorAvatarForHost(runner, player);
            Debug.Log("BasicSpawner: Actor Host joined. ActorAvatar spawn attempted.");
        }
        else
        {
            Debug.Log("BasicSpawner: Audience Client joined. No audience avatar spawned.");
        }
    }

    private void SpawnWebRtcSignalHubIfNeeded(NetworkRunner runner)
    {
        if (_webRtcSignalHubObject != null)
            return;

        if (_webRtcSignalHubPrefab == default)
        {
            Debug.LogWarning("BasicSpawner: WebRtcSignalHub prefab is not assigned.");
            return;
        }

        _webRtcSignalHubObject = runner.Spawn(
            _webRtcSignalHubPrefab,
            Vector3.zero,
            Quaternion.identity
        );

        Debug.Log("BasicSpawner: WebRtcSignalHub spawned by Actor Host.");
    }

    private void SpawnNetworkWebcamControlHubIfNeeded(NetworkRunner runner)
    {
        if (_networkWebcamControlHubObject != null)
            return;

        if (_networkWebcamControlHubPrefab == default)
        {
            Debug.LogWarning("BasicSpawner: NetworkWebcamControlHub prefab is not assigned.");
            return;
        }

        _networkWebcamControlHubObject = runner.Spawn(
            _networkWebcamControlHubPrefab,
            Vector3.zero,
            Quaternion.identity
        );

        Debug.Log("BasicSpawner: NetworkWebcamControlHub spawned by Actor Host.");
    }

    private void SpawnActorAvatarForHost(NetworkRunner runner, PlayerRef player)
    {
        if (_actorAvatarObject != null)
            return;

        if (_actorAvatarPrefab == default)
        {
            Debug.LogWarning("BasicSpawner: ActorAvatar prefab is not assigned. ActorAvatar will not be spawned.");
            return;
        }

        if (_actorSpawnPoint == null)
        {
            Debug.LogWarning("BasicSpawner: Actor spawn point is not assigned. ActorAvatar will spawn at Vector3.zero.");
        }

        Vector3 spawnPosition = _actorSpawnPoint != null
            ? _actorSpawnPoint.position
            : Vector3.zero;

        Quaternion spawnRotation = _actorSpawnPoint != null
            ? _actorSpawnPoint.rotation
            : Quaternion.identity;

        _actorAvatarObject = runner.Spawn(
            _actorAvatarPrefab,
            spawnPosition,
            spawnRotation,
            player
        );

        runner.SetPlayerObject(player, _actorAvatarObject);
        _spawnedObjects[player] = _actorAvatarObject;

        TryBindActorLocalSources(_actorAvatarObject);
    }

    private void TryBindActorLocalSources(NetworkObject actorAvatar)
    {
        ActorAvatarNetworkSync sync = actorAvatar.GetComponent<ActorAvatarNetworkSync>();

        if (sync == null)
        {
            Debug.LogWarning(
                "BasicSpawner: ActorAvatarNetworkSync not found on ActorAvatar. " +
                "This is okay if you have not added actor tracking sync yet."
            );
            return;
        }

        sync.SetLocalSources(
            _actorHeadSource,
            _actorLeftHandSource,
            _actorRightHandSource
        );

        Debug.Log("BasicSpawner: Actor local tracking sources bound to ActorAvatar.");
    }

    void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (_spawnedObjects.TryGetValue(player, out NetworkObject networkObject))
        {
            runner.Despawn(networkObject);
            _spawnedObjects.Remove(player);
            Debug.Log($"BasicSpawner: Despawned object for player: {player}");
        }
    }

    void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input)
    {
        NetworkInputData data = new NetworkInputData();
        input.Set(data);
    }

    void INetworkRunnerCallbacks.OnInputMissing(
        NetworkRunner runner,
        PlayerRef player,
        NetworkInput input)
    { }

    void INetworkRunnerCallbacks.OnShutdown(
        NetworkRunner runner,
        ShutdownReason shutdownReason)
    {
        Debug.Log($"BasicSpawner: Runner shutdown. Reason: {shutdownReason}");
    }

    void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner)
    {
        Debug.Log("BasicSpawner: Connected to server.");
    }

    void INetworkRunnerCallbacks.OnDisconnectedFromServer(
        NetworkRunner runner,
        NetDisconnectReason reason)
    {
        Debug.LogWarning($"BasicSpawner: Disconnected from server. Reason: {reason}");
    }

    void INetworkRunnerCallbacks.OnConnectRequest(
        NetworkRunner runner,
        NetworkRunnerCallbackArgs.ConnectRequest request,
        byte[] token)
    { }

    void INetworkRunnerCallbacks.OnConnectFailed(
        NetworkRunner runner,
        NetAddress remoteAddress,
        NetConnectFailedReason reason)
    {
        Debug.LogError($"BasicSpawner: Connect failed. Address: {remoteAddress}, Reason: {reason}");
    }

    void INetworkRunnerCallbacks.OnUserSimulationMessage(
        NetworkRunner runner,
        SimulationMessagePtr message)
    { }

    void INetworkRunnerCallbacks.OnSessionListUpdated(
        NetworkRunner runner,
        List<SessionInfo> sessionList)
    { }

    void INetworkRunnerCallbacks.OnCustomAuthenticationResponse(
        NetworkRunner runner,
        Dictionary<string, object> data)
    { }

    void INetworkRunnerCallbacks.OnHostMigration(
        NetworkRunner runner,
        HostMigrationToken hostMigrationToken)
    { }

    void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner runner)
    {
        Debug.Log("BasicSpawner: Network scene load done.");
    }

    void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner runner)
    {
        Debug.Log("BasicSpawner: Network scene load start.");
    }

    void INetworkRunnerCallbacks.OnObjectExitAOI(
        NetworkRunner runner,
        NetworkObject obj,
        PlayerRef player)
    { }

    void INetworkRunnerCallbacks.OnObjectEnterAOI(
        NetworkRunner runner,
        NetworkObject obj,
        PlayerRef player)
    { }

    void INetworkRunnerCallbacks.OnReliableDataReceived(
        NetworkRunner runner,
        PlayerRef player,
        ReliableKey key,
        ArraySegment<byte> data)
    { }

    void INetworkRunnerCallbacks.OnReliableDataProgress(
        NetworkRunner runner,
        PlayerRef player,
        ReliableKey key,
        float progress)
    { }
}