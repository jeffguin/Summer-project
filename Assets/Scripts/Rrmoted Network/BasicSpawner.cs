using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

public class BasicSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Player Prefabs")]
    [SerializeField] private NetworkPrefabRef _actorPrefab;
    [SerializeField] private NetworkPrefabRef _audiencePrefab;

    [Header("WebRTC Signal Hub")]
    [SerializeField] private NetworkPrefabRef _webRtcSignalHubPrefab;

    [Header("Spawn Points")]
    [SerializeField] private Transform _actorSpawnPoint;
    [SerializeField] private Transform _audienceSpawnPoint;

    private NetworkRunner _runner;
    private NetworkObject _webRtcSignalHubObject;

    private Dictionary<PlayerRef, NetworkObject> _spawnedCharacters =
        new Dictionary<PlayerRef, NetworkObject>();

    async void StartGame(GameMode mode)
    {
        _runner = GetComponent<NetworkRunner>();

        if (_runner == null)
        {
            _runner = gameObject.AddComponent<NetworkRunner>();
        }

        _runner.ProvideInput = true;
        _runner.AddCallbacks(this);

        var scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex);

        await _runner.StartGame(new StartGameArgs()
        {
            GameMode = mode,
            SessionName = "TestRoom",
            Scene = scene,
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        });
    }

    private void OnGUI()
    {
        if (_runner == null)
        {
            if (GUI.Button(new Rect(0, 0, 240, 45), "Start Audience Host"))
            {
                StartGame(GameMode.Host);
            }

            if (GUI.Button(new Rect(0, 50, 240, 45), "Join as Actor"))
            {
                StartGame(GameMode.Client);
            }
        }
    }

    void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer)
            return;

        SpawnWebRtcSignalHubIfNeeded(runner);

        // 当前 WebRTC 测试规则：
        // Host = Audience PC / Webcam Sender
        // Client = Actor / Video Receiver
        bool isAudience = player == runner.LocalPlayer;

        NetworkPrefabRef selectedPrefab =
            isAudience ? _audiencePrefab : _actorPrefab;

        Transform selectedSpawnPoint =
            isAudience ? _audienceSpawnPoint : _actorSpawnPoint;

        if (selectedSpawnPoint == null)
        {
            Debug.LogError("Spawn point is missing. Please assign Actor/Audience spawn points.");
            return;
        }

        NetworkObject playerObject = runner.Spawn(
            selectedPrefab,
            selectedSpawnPoint.position,
            selectedSpawnPoint.rotation,
            player
        );

        runner.SetPlayerObject(player, playerObject);
        _spawnedCharacters[player] = playerObject;

        Debug.Log(
            isAudience
                ? "Audience player spawned."
                : "Actor player spawned."
        );
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

        Debug.Log("WebRtcSignalHub spawned by Host.");
    }

    void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (_spawnedCharacters.TryGetValue(player, out NetworkObject networkObject))
        {
            runner.Despawn(networkObject);
            _spawnedCharacters.Remove(player);
        }
    }

    void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input)
    {
        var data = new NetworkInputData();

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
    void INetworkRunnerCallbacks.OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    void INetworkRunnerCallbacks.OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    void INetworkRunnerCallbacks.OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    void INetworkRunnerCallbacks.OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    void INetworkRunnerCallbacks.OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    void INetworkRunnerCallbacks.OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner runner) { }
    void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner runner) { }
    void INetworkRunnerCallbacks.OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    void INetworkRunnerCallbacks.OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    void INetworkRunnerCallbacks.OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    void INetworkRunnerCallbacks.OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
}