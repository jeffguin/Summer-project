using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using Meta.XR.Movement.Networking.Fusion;
using UnityEngine;

public class BasicSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    private readonly struct InteractableResetPose
    {
        public InteractableResetPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
    }

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

    [Tooltip("仅当所有客户端需要由 Fusion 加载同一个物理场景时开启。" +
             "Actor 与 Audience 使用不同本地场景时必须关闭，避免 SceneRef 0 " +
             "把 Audience Editor 切换到 Actor 场景。")]
    [SerializeField] private bool _synchronizeRoleSceneThroughFusion = false;

    [Header("Debug")]
    [Tooltip("开启后输出初始网络物体、可交互物体列表、SpawnPoint、PrefabRef、Runner 状态等调试日志。")]
    [SerializeField] private bool _debugInteractableSpawning = true;

    [Tooltip("开启后每次尝试生成可交互物体时输出更详细的列表元素检查信息。")]
    [SerializeField] private bool _verboseInteractableSpawning = true;

    [Serializable]
    private class NetworkInteractableSpawnItem
    {
        [Tooltip("仅用于 Inspector 和日志显示，例如 Cup / Ball / Knife / Plate。")]
        public string name = "Interactable";

        [Tooltip("需要由 Actor Host 生成的 Fusion Network Prefab。Prefab 根节点必须包含 NetworkObject，并且必须注册到 Fusion Network Project Config 的 Prefab Table。")]
        public NetworkPrefabRef prefab;

        [Tooltip("该物体在场景中的生成位置。建议在 Actor 场景中创建对应的 SpawnPoint 空物体。")]
        public Transform spawnPoint;

        [Tooltip("如果没有设置 SpawnPoint，则使用这个备用生成位置。")]
        public Vector3 fallbackPosition = new Vector3(0f, 1.2f, 2f);

        [Tooltip("如果没有设置 SpawnPoint，则使用这个备用生成旋转。")]
        public Vector3 fallbackEulerAngles = Vector3.zero;

        [Tooltip("是否在 Actor Host 创建房间 / 场景加载完成后自动生成。")]
        public bool spawnOnActorHostStart = true;

        [Tooltip("是否把本物体的 Input Authority 分配给 Actor Host。本项目中的可交互物体通常保持 false，由 State Authority/Host 控制。")]
        public bool assignInputAuthorityToActor = false;
    }

    [Header("Network Interactable Objects")]
    [Tooltip("由 Actor Host 自动生成并同步到 Audience Client 的可交互网络物体列表。杯子、球、盘子等都可以加入这里。")]
    [SerializeField]
    private List<NetworkInteractableSpawnItem> _networkInteractableSpawnItems =
        new List<NetworkInteractableSpawnItem>();

    private NetworkRunner _runner;

    private NetworkObject _webRtcSignalHubObject;
    private NetworkObject _networkWebcamControlHubObject;
    private NetworkObject _actorAvatarObject;

    private readonly Dictionary<int, NetworkObject> _spawnedInteractableObjects =
        new Dictionary<int, NetworkObject>();

    private readonly Dictionary<int, InteractableResetPose> _interactableResetPoses =
        new Dictionary<int, InteractableResetPose>();

    private LocalRole _localRole = LocalRole.None;

    private int _clientRetryCount = 0;
    private bool _isStartingGame = false;

    private readonly Dictionary<PlayerRef, NetworkObject> _spawnedObjects =
        new Dictionary<PlayerRef, NetworkObject>();

    public bool IsActorHostReadyForObjectReset =>
        _localRole == LocalRole.ActorHost &&
        _runner != null &&
        _runner.IsRunning &&
        _runner.IsServer;

    public int SpawnedNetworkInteractableCount
    {
        get
        {
            int validObjectCount = 0;

            foreach (NetworkObject networkObject in _spawnedInteractableObjects.Values)
            {
                if (networkObject != null && networkObject.IsValid)
                {
                    validObjectCount++;
                }
            }

            return validObjectCount;
        }
    }

    private void DebugSpawn(string message)
    {
        if (!_debugInteractableSpawning)
            return;

        Debug.Log("[BasicSpawner Spawn Debug] " + message);
    }

    private void DebugSpawnWarning(string message)
    {
        Debug.LogWarning("[BasicSpawner Spawn Debug] " + message);
    }

    private void DebugSpawnError(string message)
    {
        Debug.LogError("[BasicSpawner Spawn Debug] " + message);
    }

    private void Start()
    {
        DebugSpawn(
            $"Start called. AutoStart={_autoStart}, AutoStartAsActorHost={_autoStartAsActorHost}, " +
            $"Session={_sessionName}, SceneBuildIndex={_sceneBuildIndex}"
        );

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

        SceneRef scene = SceneRef.None;
        var startGameArgs = new StartGameArgs()
        {
            GameMode = mode,
            SessionName = _sessionName
        };

        if (_synchronizeRoleSceneThroughFusion)
        {
            scene = SceneRef.FromIndex(_sceneBuildIndex);

            NetworkSceneManagerDefault sceneManager =
                GetComponent<NetworkSceneManagerDefault>();

            if (sceneManager == null)
            {
                sceneManager =
                    gameObject.AddComponent<NetworkSceneManagerDefault>();
            }

            startGameArgs.Scene = scene;
            startGameArgs.SceneManager = sceneManager;
        }

        Debug.Log(
            $"BasicSpawner: Starting Fusion. " +
            $"Mode: {mode}, Role: {_localRole}, Session: {_sessionName}, " +
            $"SynchronizeScene: {_synchronizeRoleSceneThroughFusion}, " +
            $"SceneRef: {scene}, LocalScene: {gameObject.scene.path}"
        );

        StartGameResult result = await _runner.StartGame(startGameArgs);

        _isStartingGame = false;

        if (result.Ok)
        {
            _clientRetryCount = 0;
            Debug.Log("BasicSpawner: Fusion StartGame succeeded.");

            DebugSpawn(
                $"StartGame result OK. RunnerExists={_runner != null}, " +
                $"IsServer={(_runner != null && _runner.IsServer)}, " +
                $"LocalPlayer={(_runner != null ? _runner.LocalPlayer.ToString() : "None")}"
            );

            //if (_runner != null && _runner.IsServer)
            //{
            //    DebugSpawn("Calling TrySpawnInitialNetworkObjects immediately after StartGame succeeded.");
            //    TrySpawnInitialNetworkObjects(_runner);
            //}

            DebugSpawn("StartGame succeeded. Waiting for OnSceneLoadDone before spawning initial network objects.");
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
        _spawnedInteractableObjects.Clear();
        _interactableResetPoses.Clear();
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

        TrySpawnInitialNetworkObjects(runner);

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

    private void TrySpawnInitialNetworkObjects(NetworkRunner runner)
    {
        if (runner == null)
        {
            DebugSpawnWarning("Cannot spawn initial network objects because runner is null.");
            return;
        }

        DebugSpawn(
            $"TrySpawnInitialNetworkObjects called. " +
            $"IsServer={runner.IsServer}, LocalPlayer={runner.LocalPlayer}, " +
            $"InteractableCount={(_networkInteractableSpawnItems != null ? _networkInteractableSpawnItems.Count : -1)}"
        );

        if (!runner.IsServer)
        {
            DebugSpawn("Skip TrySpawnInitialNetworkObjects because this runner is not server.");
            return;
        }

        SpawnWebRtcSignalHubIfNeeded(runner);
        SpawnNetworkWebcamControlHubIfNeeded(runner);
        SpawnNetworkInteractableObjectsIfNeeded(runner);

        Debug.Log("BasicSpawner: Initial network objects checked/spawned by Actor Host.");
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
            player,
            (spawnRunner, spawnedObject) =>
            {
                NetworkCharacterBehaviourFusion movement =
                    spawnedObject.GetComponent<NetworkCharacterBehaviourFusion>();

                if (movement == null)
                {
                    Debug.LogError(
                        "BasicSpawner: ActorAvatar is missing NetworkCharacterBehaviourFusion."
                    );
                    return;
                }

                movement.CharacterId = 1;
                movement.MetaId = 0;
            }
        );

        runner.SetPlayerObject(player, _actorAvatarObject);
        _spawnedObjects[player] = _actorAvatarObject;

        ActorNetworkRootDriver rootDriver =
            _actorAvatarObject.GetComponent<ActorNetworkRootDriver>();

        if (rootDriver == null)
        {
            Debug.LogError(
                "BasicSpawner: ActorNetworkRootDriver is missing on ActorAvatar."
            );
        }
        else if (_actorLocalRig == null)
        {
            Debug.LogError(
                "BasicSpawner: Actor Local Rig is not assigned. " +
                "Actor root calibration cannot start."
            );
        }
        else if (_actorSpawnPoint == null)
        {
            Debug.LogError(
                "BasicSpawner: Actor Spawn Point is not assigned. " +
                "Actor root calibration cannot start."
            );
        }
        else
        {
            rootDriver.SetCalibrationReferences(
                _actorLocalRig.transform,
                _actorSpawnPoint
            );

            if (!rootDriver.Calibrate())
            {
                Debug.LogWarning(
                    "BasicSpawner: Initial actor calibration failed."
                );
            }
        }

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

        DebugSpawn(
        $"OnSceneLoadDone called. " +
        $"RunnerExists={runner != null}, " +
        $"IsServer={(runner != null && runner.IsServer)}, " +
        $"LocalPlayer={(runner != null ? runner.LocalPlayer.ToString() : "None")}"
    );

        if (runner != null && runner.IsServer)
        {
            DebugSpawn("Calling TrySpawnInitialNetworkObjects from OnSceneLoadDone.");
            TrySpawnInitialNetworkObjects(runner);
        }
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


    private void SpawnNetworkInteractableObjectsIfNeeded(NetworkRunner runner)
    {
        if (runner == null)
        {
            DebugSpawnWarning("Cannot spawn interactable objects because runner is null.");
            return;
        }

        DebugSpawn(
            $"SpawnNetworkInteractableObjectsIfNeeded called. " +
            $"IsServer={runner.IsServer}, " +
            $"ListNull={_networkInteractableSpawnItems == null}, " +
            $"ListCount={(_networkInteractableSpawnItems != null ? _networkInteractableSpawnItems.Count : -1)}, " +
            $"AlreadySpawnedCount={_spawnedInteractableObjects.Count}"
        );

        if (!runner.IsServer)
        {
            DebugSpawn("Skip interactable spawning because this runner is not server.");
            return;
        }

        if (_networkInteractableSpawnItems == null || _networkInteractableSpawnItems.Count == 0)
        {
            Debug.Log("BasicSpawner: No network interactable spawn items configured.");
            return;
        }

        for (int i = 0; i < _networkInteractableSpawnItems.Count; i++)
        {
            SpawnNetworkInteractableIfNeeded(runner, i, _networkInteractableSpawnItems[i]);
        }
    }

    private void SpawnNetworkInteractableIfNeeded(
        NetworkRunner runner,
        int itemIndex,
        NetworkInteractableSpawnItem item)
    {
        if (item == null)
        {
            DebugSpawnWarning($"Interactable item at index {itemIndex} is null.");
            return;
        }

        string itemName = GetInteractableName(item, itemIndex);

        if (_verboseInteractableSpawning)
        {
            DebugSpawn(
                $"Checking interactable item. " +
                $"Index={itemIndex}, Name={itemName}, " +
                $"SpawnOnActorHostStart={item.spawnOnActorHostStart}, " +
                $"AssignInputAuthorityToActor={item.assignInputAuthorityToActor}, " +
                $"PrefabIsDefault={item.prefab == default}, " +
                $"SpawnPoint={(item.spawnPoint != null ? item.spawnPoint.name : "None")}, " +
                $"FallbackPosition={item.fallbackPosition}, " +
                $"FallbackEuler={item.fallbackEulerAngles}, " +
                $"AlreadySpawned={_spawnedInteractableObjects.ContainsKey(itemIndex)}"
            );
        }

        if (!item.spawnOnActorHostStart)
        {
            DebugSpawn($"Skip interactable '{itemName}' because spawnOnActorHostStart is false.");
            return;
        }

        if (_spawnedInteractableObjects.TryGetValue(itemIndex, out NetworkObject existingObject))
        {
            DebugSpawn(
                $"Skip interactable '{itemName}' because it is already spawned. " +
                $"ExistingObject={(existingObject != null ? existingObject.name : "NullReference")}"
            );
            return;
        }

        if (item.prefab == default)
        {
            DebugSpawnWarning(
                $"Interactable item '{itemName}' has no NetworkPrefabRef assigned. " +
                $"Check BasicSpawner Inspector list element {itemIndex}."
            );
            return;
        }

        Vector3 spawnPosition = item.spawnPoint != null
            ? item.spawnPoint.position
            : item.fallbackPosition;

        Quaternion spawnRotation = item.spawnPoint != null
            ? item.spawnPoint.rotation
            : Quaternion.Euler(item.fallbackEulerAngles);

        PlayerRef? inputAuthority = item.assignInputAuthorityToActor
            ? runner.LocalPlayer
            : null;

        DebugSpawn(
            $"Trying to spawn interactable '{itemName}'. " +
            $"Index={itemIndex}, Position={spawnPosition}, Rotation={spawnRotation.eulerAngles}, " +
            $"InputAuthority={(inputAuthority.HasValue ? inputAuthority.Value.ToString() : "None")}"
        );

        NetworkObject spawnedObject = null;

        try
        {
            spawnedObject = inputAuthority.HasValue
                ? runner.Spawn(item.prefab, spawnPosition, spawnRotation, inputAuthority.Value)
                : runner.Spawn(item.prefab, spawnPosition, spawnRotation, inputAuthority: null);
        }
        catch (Exception exception)
        {
            DebugSpawnError(
                $"Exception while spawning interactable '{itemName}'. " +
                $"This usually means the prefab is not a valid Fusion Network Prefab, " +
                $"is not registered in Network Project Config / Prefab Table, " +
                $"or the prefab root does not contain a NetworkObject. " +
                $"Exception={exception}"
            );
            return;
        }

        if (spawnedObject == null)
        {
            DebugSpawnError(
                $"Runner.Spawn returned null for interactable '{itemName}'. " +
                $"Check Prefab Table registration and NetworkObject on prefab root."
            );
            return;
        }

        _spawnedInteractableObjects[itemIndex] = spawnedObject;
        _interactableResetPoses[itemIndex] =
            new InteractableResetPose(spawnPosition, spawnRotation);

        Debug.Log(
            $"BasicSpawner: Network interactable spawned. " +
            $"Index: {itemIndex}, Name: {itemName}, Object: {spawnedObject.name}, " +
            $"Position: {spawnedObject.transform.position}, Rotation: {spawnedObject.transform.rotation.eulerAngles}"
        );
    }

    /// <summary>
    /// 演员 Host 的日常恢复入口。保留原 NetworkObject / NetworkId，
    /// 由 State Authority 强制释放并 Teleport 回首次生成位姿。
    /// </summary>
    /// <returns>成功重置的网络交互物体数量。</returns>
    public int ResetAllNetworkInteractables()
    {
        if (!IsActorHostReadyForObjectReset)
        {
            Debug.LogWarning(
                "BasicSpawner: Reset All ignored. " +
                "Only the running Actor Host / State Authority can reset interactable objects."
            );
            return 0;
        }

        int resetCount = 0;

        foreach (KeyValuePair<int, NetworkObject> spawnedEntry in _spawnedInteractableObjects)
        {
            int itemIndex = spawnedEntry.Key;
            NetworkObject networkObject = spawnedEntry.Value;

            if (networkObject == null || !networkObject.IsValid)
            {
                Debug.LogWarning(
                    $"BasicSpawner: Reset All skipped item index {itemIndex} " +
                    "because its NetworkObject is no longer valid."
                );
                continue;
            }

            if (!_interactableResetPoses.TryGetValue(
                    itemIndex,
                    out InteractableResetPose resetPose))
            {
                Debug.LogWarning(
                    $"BasicSpawner: Reset All skipped '{networkObject.name}' " +
                    "because its initial spawn pose was not recorded."
                );
                continue;
            }

            NetworkPhysicalGrabbable grabbable =
                networkObject.GetComponent<NetworkPhysicalGrabbable>();

            if (grabbable == null)
            {
                Debug.LogWarning(
                    $"BasicSpawner: Reset All skipped '{networkObject.name}' " +
                    "because it has no NetworkPhysicalGrabbable component."
                );
                continue;
            }

            if (grabbable.ForceResetToPose(resetPose.Position, resetPose.Rotation))
            {
                resetCount++;
            }
        }

        Debug.Log(
            $"BasicSpawner: Reset All completed. " +
            $"Reset={resetCount}, Tracked={_spawnedInteractableObjects.Count}."
        );

        return resetCount;
    }

    private string GetInteractableName(NetworkInteractableSpawnItem item, int itemIndex)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.name))
        {
            return $"Interactable_{itemIndex}";
        }

        return item.name;
    }

}
