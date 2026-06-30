using System;
using Fusion;
using UnityEngine;

public class WebRtcSignalHub : NetworkBehaviour
{
    public static WebRtcSignalHub Instance { get; private set; }

    public event Action<PlayerRef, string, string> OnSignalReceived;

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
        if (target == PlayerRef.None)
        {
            Debug.LogWarning("WebRtcSignalHub: target is None.");
            return;
        }

        RPC_SendSignal(target, Runner.LocalPlayer, type, payload);
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_SendSignal(PlayerRef target, PlayerRef from, string type, string payload)
    {
        if (Runner == null)
            return;

        if (Runner.LocalPlayer != target)
            return;

        Debug.Log("WebRtcSignalHub received signal. Type: " + type + ", From: " + from);

        OnSignalReceived?.Invoke(from, type, payload);
    }
}