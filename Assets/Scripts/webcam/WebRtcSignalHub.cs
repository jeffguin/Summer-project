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
    }

    private void Awake()
    {
        Instance = this;
    }

    public override void Spawned()
    {
        Instance = this;
        Debug.Log("WebRtcSignalHub spawned. Local player: " + Runner.LocalPlayer);
    }

    public PlayerRef GetOtherPlayer()
    {
        if (Runner == null)
            return PlayerRef.None;

        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            if (player != Runner.LocalPlayer)
                return player;
        }

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
            Debug.LogWarning("WebRtcSignalHub: target is None.");
            return;
        }

        if (string.IsNullOrEmpty(payload))
        {
            Debug.LogWarning("WebRtcSignalHub: payload is empty.");
            return;
        }

        if (maxChunkSize < 100)
            maxChunkSize = 100;

        int signalId = nextSignalId++;
        int totalChunks = Mathf.CeilToInt(payload.Length / (float)maxChunkSize);

        Debug.Log(
            $"WebRtcSignalHub: Sending signal. " +
            $"Type: {type}, PayloadLength: {payload.Length}, Chunks: {totalChunks}, Target: {target}"
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
            return;

        if (Runner.LocalPlayer != target)
            return;

        if (string.IsNullOrEmpty(chunk))
            return;

        string key = from.ToString() + "_" + signalId;

        if (!chunkBuffers.TryGetValue(key, out SignalChunkBuffer buffer))
        {
            buffer = new SignalChunkBuffer
            {
                From = from,
                Type = type,
                TotalChunks = totalChunks,
                Chunks = new string[totalChunks],
                ReceivedCount = 0
            };

            chunkBuffers[key] = buffer;
        }

        if (chunkIndex < 0 || chunkIndex >= buffer.TotalChunks)
        {
            Debug.LogWarning("WebRtcSignalHub: Invalid chunk index: " + chunkIndex);
            return;
        }

        if (buffer.Chunks[chunkIndex] == null)
        {
            buffer.Chunks[chunkIndex] = chunk;
            buffer.ReceivedCount++;
        }

        if (buffer.ReceivedCount < buffer.TotalChunks)
            return;

        StringBuilder builder = new StringBuilder();

        for (int i = 0; i < buffer.TotalChunks; i++)
        {
            if (buffer.Chunks[i] == null)
            {
                Debug.LogWarning("WebRtcSignalHub: Missing chunk: " + i);
                return;
            }

            builder.Append(buffer.Chunks[i]);
        }

        string fullPayload = builder.ToString();

        chunkBuffers.Remove(key);

        Debug.Log(
            $"WebRtcSignalHub: Reassembled signal. " +
            $"Type: {buffer.Type}, PayloadLength: {fullPayload.Length}, From: {buffer.From}"
        );

        OnSignalReceived?.Invoke(buffer.From, buffer.Type, fullPayload);
    }
}