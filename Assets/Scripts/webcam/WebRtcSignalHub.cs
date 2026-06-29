using System;
using System.Collections.Generic;

using Fusion;
using UnityEngine;

public class WebRtcSignalHub : NetworkBehaviour
{
    public static WebRtcSignalHub Instance { get; private set; }

    public event Action<PlayerRef, string, string> OnSignalReceived;

    private const int ChunkSize = 350;

    private class ChunkBuffer
    {
        public string Type;
        public string[] Chunks;
        public int ReceivedCount;
        public PlayerRef Source;
    }

    private readonly Dictionary<string, ChunkBuffer> buffers = new Dictionary<string, ChunkBuffer>();

    public override void Spawned()
    {
        Instance = this;
        Debug.Log("WebRtcSignalHub spawned.");
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public PlayerRef GetOtherPlayer()
    {
        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            if (player != Runner.LocalPlayer)
            {
                return player;
            }
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
            Debug.LogWarning("WebRtcSignalHub: Target player is none.");
            return;
        }

        if (string.IsNullOrEmpty(payload))
        {
            Debug.LogWarning("WebRtcSignalHub: Payload is empty.");
            return;
        }

        string messageId = Guid.NewGuid().ToString("N");
        int total = Mathf.CeilToInt(payload.Length / (float)ChunkSize);

        for (int i = 0; i < total; i++)
        {
            int start = i * ChunkSize;
            int length = Mathf.Min(ChunkSize, payload.Length - start);
            string chunk = payload.Substring(start, length);

            RPC_SendSignalChunk(
                target.RawEncoded,
                Runner.LocalPlayer.RawEncoded,
                messageId,
                type,
                i,
                total,
                chunk
            );
        }
    }

    [Rpc(RpcSources.All, RpcTargets.All, Channel = RpcChannel.Reliable, TickAligned = false)]
    private void RPC_SendSignalChunk(
        int targetRaw,
        int sourceRaw,
        string messageId,
        string type,
        int index,
        int total,
        string chunk)
    {
        if (Runner == null)
            return;

        if (Runner.LocalPlayer.RawEncoded != targetRaw)
            return;

        PlayerRef source = PlayerRef.FromEncoded(sourceRaw);
        ReceiveChunk(source, messageId, type, index, total, chunk);
    }

    private void ReceiveChunk(
        PlayerRef source,
        string messageId,
        string type,
        int index,
        int total,
        string chunk)
    {
        string key = source.RawEncoded + "_" + messageId;

        if (!buffers.TryGetValue(key, out ChunkBuffer buffer))
        {
            buffer = new ChunkBuffer
            {
                Type = type,
                Source = source,
                Chunks = new string[total],
                ReceivedCount = 0
            };

            buffers.Add(key, buffer);
        }

        if (index < 0 || index >= buffer.Chunks.Length)
            return;

        if (buffer.Chunks[index] == null)
        {
            buffer.Chunks[index] = chunk;
            buffer.ReceivedCount++;
        }

        if (buffer.ReceivedCount == buffer.Chunks.Length)
        {
            string fullPayload = string.Concat(buffer.Chunks);
            buffers.Remove(key);

            Debug.Log("WebRTC signal received: " + buffer.Type);

            OnSignalReceived?.Invoke(buffer.Source, buffer.Type, fullPayload);
        }
    }
}