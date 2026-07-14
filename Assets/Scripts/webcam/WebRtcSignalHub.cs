using System;
using System.Collections.Generic;
using System.Text;
using Fusion;
using UnityEngine;

public class WebRtcSignalHub : NetworkBehaviour
{
    public static WebRtcSignalHub Instance { get; private set; }

    public event Action<PlayerRef, string, string> OnSignalReceived;

    [Header("Chunk Settings")]
    [SerializeField] private int maxChunkSize = 250;
    [SerializeField] private float incompleteSignalTimeoutSeconds = 30f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;
    [SerializeField] private bool debugRpcReceiveLog = true;

    private int nextSignalId = 1;

    private readonly Dictionary<string, SignalChunkBuffer> chunkBuffers =
        new Dictionary<string, SignalChunkBuffer>();

    private class SignalChunkBuffer
    {
        public PlayerRef From;
        public string Type;
        public int TotalChunks;
        public string[] Chunks;
        public int ReceivedCount;
        public float LastUpdatedTime;
    }

    private void Update()
    {
        if (chunkBuffers.Count == 0)
            return;

        float now = Time.realtimeSinceStartup;
        List<string> expiredKeys = null;

        foreach (KeyValuePair<string, SignalChunkBuffer> entry in chunkBuffers)
        {
            if (now - entry.Value.LastUpdatedTime <= incompleteSignalTimeoutSeconds)
                continue;

            expiredKeys ??= new List<string>();
            expiredKeys.Add(entry.Key);
        }

        if (expiredKeys == null)
            return;

        foreach (string key in expiredKeys)
        {
            Debug.LogWarning("WebRtcSignalHub: Discarding timed-out incomplete signal " + key);
            chunkBuffers.Remove(key);
        }
    }

    private void Awake()
    {
        Instance = this;
        DebugMessage("Awake. Instance assigned. GameObject = " + gameObject.name);
    }

    public override void Spawned()
    {
        Instance = this;

        DebugMessage(
            "Spawned. " +
            "LocalPlayer = " + Runner.LocalPlayer +
            ", IsServer = " + Runner.IsServer +
            ", Object = " + gameObject.name
        );
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this)
        {
            Instance = null;
        }

        chunkBuffers.Clear();

        DebugMessage("Despawned. Instance cleared.");
    }

    public PlayerRef GetOtherPlayer()
    {
        if (Runner == null)
        {
            DebugMessage("GetOtherPlayer failed. Runner is null.");
            return PlayerRef.None;
        }

        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            if (player != Runner.LocalPlayer)
            {
                DebugMessage(
                    "GetOtherPlayer result = " + player +
                    ", LocalPlayer = " + Runner.LocalPlayer
                );

                return player;
            }
        }

        DebugMessage(
            "GetOtherPlayer failed. No other player found. " +
            "LocalPlayer = " + Runner.LocalPlayer +
            ", ActivePlayersCount may be 1."
        );

        return PlayerRef.None;
    }

    public void SendSignal(PlayerRef target, string type, string payload)
    {
        if (Runner == null)
        {
            Debug.LogWarning("WebRtcSignalHub: Runner is null.");
            return;
        }

        if (target == PlayerRef.None)
        {
            Debug.LogWarning("WebRtcSignalHub: target is None. Type = " + type);
            return;
        }

        if (string.IsNullOrEmpty(type))
        {
            Debug.LogWarning("WebRtcSignalHub: type is empty.");
            return;
        }

        if (string.IsNullOrEmpty(payload))
        {
            Debug.LogWarning("WebRtcSignalHub: payload is empty. Type = " + type);
            return;
        }

        if (maxChunkSize < 100)
            maxChunkSize = 100;

        int signalId = nextSignalId++;
        int totalChunks = Mathf.CeilToInt(payload.Length / (float)maxChunkSize);

        DebugMessage(
            "Sending signal. " +
            "Type = " + type +
            ", SignalId = " + signalId +
            ", PayloadLength = " + payload.Length +
            ", Chunks = " + totalChunks +
            ", From = " + Runner.LocalPlayer +
            ", Target = " + target +
            ", IsServer = " + Runner.IsServer
        );

        for (int i = 0; i < totalChunks; i++)
        {
            int start = i * maxChunkSize;
            int length = Mathf.Min(maxChunkSize, payload.Length - start);
            string chunk = payload.Substring(start, length);

            RPC_SendSignalChunk(
                target,
                Runner.LocalPlayer,
                type,
                signalId,
                i,
                totalChunks,
                chunk
            );
        }
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_SendSignalChunk(
        PlayerRef target,
        PlayerRef from,
        string type,
        int signalId,
        int chunkIndex,
        int totalChunks,
        string chunk)
    {
        if (Runner == null)
        {
            Debug.LogWarning("WebRtcSignalHub: RPC received but Runner is null. Type = " + type);
            return;
        }

        if (debugRpcReceiveLog)
        {
            DebugMessage(
                "RPC received. " +
                "Type = " + type +
                ", SignalId = " + signalId +
                ", Chunk = " + chunkIndex + "/" + totalChunks +
                ", From = " + from +
                ", Target = " + target +
                ", LocalPlayer = " + Runner.LocalPlayer +
                ", IsServer = " + Runner.IsServer +
                ", ChunkLength = " + (chunk != null ? chunk.Length : 0)
            );
        }

        if (Runner.LocalPlayer != target)
        {
            if (debugRpcReceiveLog)
            {
                DebugMessage(
                    "RPC ignored because local player is not target. " +
                    "Type = " + type +
                    ", Target = " + target +
                    ", LocalPlayer = " + Runner.LocalPlayer
                );
            }

            return;
        }

        if (string.IsNullOrEmpty(chunk))
        {
            Debug.LogWarning(
                "WebRtcSignalHub: RPC chunk is empty. " +
                "Type = " + type +
                ", SignalId = " + signalId
            );

            return;
        }

        if (totalChunks <= 0 || totalChunks > 4096)
        {
            Debug.LogWarning(
                "WebRtcSignalHub: Invalid total chunk count. Type = " + type +
                ", TotalChunks = " + totalChunks
            );
            return;
        }

        string key = from + "_" + type + "_" + signalId;

        if (!chunkBuffers.TryGetValue(key, out SignalChunkBuffer buffer))
        {
            buffer = new SignalChunkBuffer
            {
                From = from,
                Type = type,
                TotalChunks = totalChunks,
                Chunks = new string[totalChunks],
                ReceivedCount = 0,
                LastUpdatedTime = Time.realtimeSinceStartup
            };

            chunkBuffers[key] = buffer;

            DebugMessage(
                "Created chunk buffer. " +
                "Key = " + key +
                ", Type = " + type +
                ", TotalChunks = " + totalChunks +
                ", From = " + from
            );
        }

        if (chunkIndex < 0 || chunkIndex >= buffer.TotalChunks)
        {
            Debug.LogWarning(
                "WebRtcSignalHub: Invalid chunk index. " +
                "Type = " + type +
                ", SignalId = " + signalId +
                ", ChunkIndex = " + chunkIndex +
                ", TotalChunks = " + buffer.TotalChunks
            );

            return;
        }

        if (buffer.Chunks[chunkIndex] == null)
        {
            buffer.Chunks[chunkIndex] = chunk;
            buffer.ReceivedCount++;
            buffer.LastUpdatedTime = Time.realtimeSinceStartup;

            DebugMessage(
                "Chunk stored. " +
                "Type = " + type +
                ", SignalId = " + signalId +
                ", Received = " + buffer.ReceivedCount + "/" + buffer.TotalChunks
            );
        }
        else
        {
            DebugMessage(
                "Duplicate chunk ignored. " +
                "Type = " + type +
                ", SignalId = " + signalId +
                ", ChunkIndex = " + chunkIndex
            );
        }

        if (buffer.ReceivedCount < buffer.TotalChunks)
            return;

        StringBuilder builder = new StringBuilder();

        for (int i = 0; i < buffer.TotalChunks; i++)
        {
            if (buffer.Chunks[i] == null)
            {
                Debug.LogWarning(
                    "WebRtcSignalHub: Missing chunk. " +
                    "Type = " + buffer.Type +
                    ", SignalId = " + signalId +
                    ", MissingChunk = " + i
                );

                return;
            }

            builder.Append(buffer.Chunks[i]);
        }

        string fullPayload = builder.ToString();

        chunkBuffers.Remove(key);

        DebugMessage(
            "Reassembled signal. " +
            "Type = " + buffer.Type +
            ", SignalId = " + signalId +
            ", PayloadLength = " + fullPayload.Length +
            ", From = " + buffer.From +
            ", LocalPlayer = " + Runner.LocalPlayer
        );

        if (OnSignalReceived == null)
        {
            Debug.LogWarning(
                "WebRtcSignalHub: Signal reassembled but OnSignalReceived has no subscribers. " +
                "Type = " + buffer.Type +
                ", From = " + buffer.From
            );
        }

        OnSignalReceived?.Invoke(buffer.From, buffer.Type, fullPayload);
    }

    private void DebugMessage(string message)
    {
        if (!debugLog)
            return;

        Debug.Log("WebRtcSignalHub: " + message);
    }
}
