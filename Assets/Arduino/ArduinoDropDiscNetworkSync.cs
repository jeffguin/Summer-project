using Fusion;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public sealed class ArduinoDropDiscNetworkSync : NetworkBehaviour
{
    [SerializeField] private ArduinoController controller;

    public bool IsNetworkSpawned => Object != null && Object.IsValid;


    private void Awake()
    {
        EnsureController();
    }


    public override void Spawned()
    {
        EnsureController();

        if (controller == null)
        {
            Debug.LogError(
                $"[ArduinoDropDiscNetworkSync] {name}: " +
                "ArduinoController was not found in the network prefab hierarchy."
            );
            return;
        }

        // Every peer receives this network object. Only Windows/Editor peers
        // open the COM port; the Quest host keeps State Authority.
        controller.ConfigureNetworkPeer(true);

        Debug.Log(
            $"[ArduinoDropDiscNetworkSync] {name}: Spawned. " +
            $"HasStateAuthority={Object.HasStateAuthority}"
        );
    }


    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (controller != null)
        {
            controller.ConfigureNetworkPeer(false);
        }
    }


    public void RequestSpawnItemFromHardwarePeer()
    {
        if (!IsNetworkSpawned)
            return;

        if (Object.HasStateAuthority)
        {
            TrySpawnItemFromStateAuthority();
            return;
        }

        RPC_RequestSpawnItemFromHardwarePeer();
    }


    [Rpc(
        RpcSources.All,
        RpcTargets.StateAuthority,
        Channel = RpcChannel.Reliable)]
    private void RPC_RequestSpawnItemFromHardwarePeer(RpcInfo info = default)
    {
        Debug.Log(
            $"[ArduinoDropDiscNetworkSync] {name}: " +
            $"Spawn requested by hardware peer {info.Source}."
        );

        TrySpawnItemFromStateAuthority();
    }


    public void TrySpawnItemFromStateAuthority()
    {
        if (!CanRunAuthoritativeAction())
            return;

        if (!controller.TryGetItemSpawnData(
                out GameObject prefab,
                out Vector3 spawnPosition,
                out Quaternion spawnRotation))
        {
            return;
        }

        NetworkObject prefabNetworkObject = prefab.GetComponent<NetworkObject>();

        if (prefabNetworkObject == null)
        {
            Debug.LogError(
                $"[ArduinoDropDiscNetworkSync] {name}: " +
                $"Item prefab '{prefab.name}' has no NetworkObject."
            );
            return;
        }

        //Spawns item using the prefab's NetworkObject. The spawned object will be automatically synchronized across all clients.
        NetworkObject spawnedObject = Runner.Spawn(
            prefabNetworkObject,
            spawnPosition,
            spawnRotation,
            inputAuthority: null
        );

        if (spawnedObject == null)
        {
            Debug.LogError(
                $"[ArduinoDropDiscNetworkSync] {name}: " +
                $"Runner.Spawn returned null for '{prefab.name}'."
            );
            return;
        }

        controller.RegisterNetworkSpawnedItem(spawnedObject.gameObject);

        RPC_PlaySweetDropAnimation();
    }


    [Rpc(
        RpcSources.StateAuthority,
        RpcTargets.All,
        Channel = RpcChannel.Reliable)]
    private void RPC_PlaySweetDropAnimation()
    {
        EnsureController();

        if (controller == null)
        {
            Debug.LogError(
                $"[ArduinoDropDiscNetworkSync] {name}: " +
                "Cannot play Sweet drop animation because ArduinoController is missing."
            );
            return;
        }

        controller.PlaySweetDropAnimationFromNetwork();
    }


    public void TryCollectSweetFromStateAuthority(Collider other)
    {
        if (!CanRunAuthoritativeAction() || other == null)
            return;

        GameObject sweetObject = other.gameObject;
        NetworkObject sweetNetworkObject =
            other.GetComponentInParent<NetworkObject>();

        if (sweetNetworkObject != null &&
            sweetNetworkObject != Object &&
            !sweetNetworkObject.IsValid)
        {
            return;
        }

        controller.HandleSweetCollectedOnStateAuthority(sweetObject);

        // The reliable RPC invokes locally on the Quest host and on every
        // audience proxy. Windows owns the serial connection; all peers play
        // the same animation once.
        RPC_HandleSweetCollectedForEveryone();

        DespawnCollectedSweet(sweetObject, sweetNetworkObject);
    }


    [Rpc(
        RpcSources.StateAuthority,
        RpcTargets.All,
        Channel = RpcChannel.Reliable)]
    private void RPC_HandleSweetCollectedForEveryone()
    {
        EnsureController();

        if (controller == null)
        {
            Debug.LogError(
                $"[ArduinoDropDiscNetworkSync] {name}: " +
                "Cannot handle the collected Sweet because ArduinoController is missing."
            );
            return;
        }

        controller.SendSweetCollectedToHardwareFromNetwork();
        controller.PlayFallingAnimationFromNetwork();
    }


    private bool CanRunAuthoritativeAction()
    {
        if (!IsNetworkSpawned || !Object.HasStateAuthority)
            return false;

        EnsureController();

        if (controller != null)
            return true;

        Debug.LogError(
            $"[ArduinoDropDiscNetworkSync] {name}: " +
            "ArduinoController is missing."
        );
        return false;
    }


    private void DespawnCollectedSweet(
        GameObject sweetObject,
        NetworkObject sweetNetworkObject)
    {
        if (sweetNetworkObject == Object)
        {
            Debug.LogError(
                $"[ArduinoDropDiscNetworkSync] {name}: " +
                "Refusing to despawn the dispenser itself. " +
                "Check that the collected Sweet has its own NetworkObject."
            );
            return;
        }

        if (sweetNetworkObject != null && sweetNetworkObject.IsValid)
        {
            if (!sweetNetworkObject.HasStateAuthority)
            {
                Debug.LogWarning(
                    $"[ArduinoDropDiscNetworkSync] {name}: " +
                    $"Cannot despawn '{sweetNetworkObject.name}' without State Authority."
                );
                return;
            }

            Runner.Despawn(sweetNetworkObject);
            return;
        }

        // Preserve the original behaviour for a non-networked Sweet used by
        // a local test setup. Networked gameplay objects take the branch above.
        Destroy(sweetObject);
    }


    private void EnsureController()
    {
        if (controller == null)
        {
            controller = GetComponentInChildren<ArduinoController>(true);
        }
    }
}