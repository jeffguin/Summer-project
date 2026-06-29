using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class WebRtcSignalingRelay : SimulationBehaviour
{
    public static WebRtcSignalingRelay Instance { get; private set; }

    public event Action<PlayerRef, string, string> OnSignalReceived;

    private const int ChunkSize = 350;

    private class ChunkBuffer
    {
        public string Type;
        public string[] Chunks;
        public int ReceivedCount;
    }

    private readonly Dictionary<string, ChunkBuffer> _buffers = new Dictionary<string, ChunkBuffer>();

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public PlayerRef GetOtherPlayer()
    {
        if (Runner == null)
            return PlayerRef.None;

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
            Debug.LogWarning("WebRtcSignalingRelay: Runner is null.");
            return;
        }

        if (target == PlayerRef.None)
        {
            Debug.LogWarning("WebRtcSignalingRelay: Target player is none.");
            return;
        }

        if (string.IsNullOrEmpty(payload))
        {
            Debug.LogWarning("WebRtcSignalingRelay: Empty payload.");
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
                Runner,
                target,
                messageId,
                type,
                i,
                total,
                chunk
            );
        }
    }

    [Rpc(
        sources: RpcSources.All,
        targets: RpcTargets.All,
        TickAligned = false,
        HostMode = RpcHostMode.SourceIsHostPlayer
    )]
    public static void RPC_SendSignalChunk(
        NetworkRunner runner,
        [RpcTarget] PlayerRef target,
        string messageId,
        string type,
        int index,
        int total,
        string chunk,
        RpcInfo info = default)
    {
        if (Instance == null)
            return;

        Instance.ReceiveChunk(info.Source, messageId, type, index, total, chunk);
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

        if (!_buffers.TryGetValue(key, out ChunkBuffer buffer))
        {
            buffer = new ChunkBuffer
            {
                Type = type,
                Chunks = new string[total],
                ReceivedCount = 0
            };

            _buffers.Add(key, buffer);
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
            _buffers.Remove(key);

            OnSignalReceived?.Invoke(source, buffer.Type, fullPayload);
        }
    }
}