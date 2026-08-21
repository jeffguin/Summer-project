using Fusion;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class WindowsToHeadsetSpawnBridge : NetworkBehaviour
{
    private ArduinoDropDiscNetworkSync headsetNetworkSync;


    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            FindHeadsetNetworkSync();
        }

        Debug.Log(
            "Spawn Bridge ready. State Authority: " +
            Object.HasStateAuthority
        );
    }


    public void RequestSpawn()
    {
        if (Object == null || !Object.IsValid)
        {
            Debug.LogError("Spawn Bridge is not network spawned.");
            return;
        }

        Debug.Log("Windows requesting object spawn.");

        if (Object.HasStateAuthority)
        {
            SpawnOnHeadset();
            return;
        }

        RPC_RequestSpawn();
    }


    [Rpc(
        RpcSources.All,
        RpcTargets.StateAuthority,
        Channel = RpcChannel.Reliable)]
    private void RPC_RequestSpawn()
    {
        Debug.Log("Headset received spawn request.");

        SpawnOnHeadset();
    }


    private void SpawnOnHeadset()
    {
        if (!Object.HasStateAuthority)
            return;

        if (headsetNetworkSync == null)
        {
            FindHeadsetNetworkSync();
        }

        if (headsetNetworkSync == null)
        {
            Debug.LogError(
                "Could not find ArduinoDropDiscNetworkSync on headset."
            );
            return;
        }

        headsetNetworkSync.TrySpawnItemFromStateAuthority();
    }


    private void FindHeadsetNetworkSync()
    {
        headsetNetworkSync =
            FindFirstObjectByType<ArduinoDropDiscNetworkSync>();
    }
}