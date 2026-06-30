using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

public class BasicSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    private enum LocalRole
    {
        None,
        ActorHost,
        AudienceClient
    }

    [Header("Network Prefabs")]
    [SerializeField] private NetworkPrefabRef _actorAvatarPrefab;

    [Header("WebRTC Signal Hub")]
    [SerializeField] private NetworkPrefabRef _webRtcSignalHubPrefab;

    [Header("Spawn Points")]
    [SerializeField] private Transform _actorSpawnPoint;

    [Header("Local Systems")]
    [Tooltip("演员端本地系统。后续放 Quest Pro / mocap / face / eye / hand tracking rig。")]
    [SerializeField] private GameObject _actorLocalRig;

    [Tooltip("观众端本地 Fish Tank 系统。拖入 AudienceFishTankRig 根物体。")]
    [SerializeField] private GameObject _audienceFishTankRig;

    [Header("Actor Local Sources")]
    [Tooltip("演员头部追踪源。后续可绑定 CenterEyeAnchor。")]
    [SerializeField] private Transform _actorHeadSource;

    [Tooltip("演员左手追踪源。后续可绑定 LeftHandAnchor。")]
    [SerializeField] private Transform _actorLeftHandSource;

    [Tooltip("演员右手追踪源。后续可绑定 RightHandAnchor。")]
    [SerializeField] private Transform _actorRightHandSource;

    [Header("Session Settings")]
    [SerializeField] private string _sessionName = "TestRoom";

    private NetworkRunner _runner;
    private NetworkObject _webRtcSignalHubObject;
    private NetworkObject _actorAvatarObject;

    private LocalRole _localRole = LocalRole.None;

    private readonly Dictionary<PlayerRef, NetworkObject> _spawnedObjects =
        new Dictionary<PlayerRef, NetworkObject>();

    private async void StartGame(GameMode mode, LocalRole role)
    {
        _localRole = role;
        ApplyLocalRole();

        _runner = GetComponent<NetworkRunner>();

        if (_runner == null)
        {
            _runner = gameObject.AddComponent<NetworkRunner>();
        }

        _runner.ProvideInput = true;
        _runner.AddCallbacks(this);

        SceneRef scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex);

        Debug.Log($"Starting Fusion. Mode: {mode}, Role: {_localRole}");

        await _runner.StartGame(new StartGameArgs()
        {
            GameMode = mode,
            SessionName = _sessionName,
            Scene = scene,
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        });
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

        Debug.Log($"Local role applied: {_localRole}");
    }

    private void OnGUI()
    {
        if (_runner != null)
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
        if (!runner.IsServer)
            return;

        SpawnWebRtcSignalHubIfNeeded(runner);

        bool isActorHostPlayer = player == runner.LocalPlayer;

        if (isActorHostPlayer)
        {
            SpawnActorAvatarForHost(runner, player);
            Debug.Log("Actor Host joined. ActorAvatar spawned.");
        }
        else
        {
            Debug.Log("Audience Client joined. No audience avatar spawned.");
        }
    }

    private void SpawnActorAvatarForHost(NetworkRunner runner, PlayerRef player)
    {
        if (_actorAvatarObject != null)
            return;

        if (_actorAvatarPrefab == default)
        {
            Debug.LogWarning("ActorAvatar prefab is not assigned. ActorAvatar will not be spawned.");
            return;
        }

        if (_actorSpawnPoint == null)
        {
            Debug.LogWarning("Actor spawn point is not assigned. ActorAvatar will spawn at Vector3.zero.");
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
                "ActorAvatarNetworkSync not found on ActorAvatar. " +
                "This is okay if you have not added actor tracking sync yet."
            );
            return;
        }

        sync.SetLocalSources(
            _actorHeadSource,
            _actorLeftHandSource,
            _actorRightHandSource
        );

        Debug.Log("Actor local tracking sources bound to ActorAvatar.");
    }

    private void SpawnWebRtcSignalHubIfNeeded(NetworkRunner runner)
    {
        if (_webRtcSignalHubObject != null)
            return;

        if (_webRtcSignalHubPrefab == default)
        {
            Debug.LogWarning("WebRtcSignalHub prefab is not assigned.");
            return;
        }

        _webRtcSignalHubObject = runner.Spawn(
            _webRtcSignalHubPrefab,
            Vector3.zero,
            Quaternion.identity
        );

        Debug.Log("WebRtcSignalHub spawned by Actor Host.");
    }

    void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (_spawnedObjects.TryGetValue(player, out NetworkObject networkObject))
        {
            runner.Despawn(networkObject);
            _spawnedObjects.Remove(player);
        }
    }

    void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input)
    {
        NetworkInputData data = new NetworkInputData();

        if (Input.GetKey(KeyCode.W))
            data.Direction += Vector3.forward;

        if (Input.GetKey(KeyCode.S))
            data.Direction += Vector3.back;

        if (Input.GetKey(KeyCode.A))
            data.Direction += Vector3.left;

        if (Input.GetKey(KeyCode.D))
            data.Direction += Vector3.right;

        input.Set(data);
    }

    void INetworkRunnerCallbacks.OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

    void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }

    void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner) { }

    void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }

    void INetworkRunnerCallbacks.OnConnectRequest(
        NetworkRunner runner,
        NetworkRunnerCallbackArgs.ConnectRequest request,
        byte[] token)
    { }

    void INetworkRunnerCallbacks.OnConnectFailed(
        NetworkRunner runner,
        NetAddress remoteAddress,
        NetConnectFailedReason reason)
    { }

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

    void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner runner) { }

    void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner runner) { }

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