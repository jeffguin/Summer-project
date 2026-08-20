using Fusion;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public sealed class ArduinoDropDiscNetworkSync : NetworkBehaviour
{
    [SerializeField] private ArduinoController controller;
    [SerializeField] private WindowsToHeadsetArduino windowsToHeadsetArduino;

    public bool IsNetworkSpawned => Object != null && Object.IsValid;


    private void Awake()
    {
        EnsureController();
        EnsureWindowsArduino();
    }


    public override void Spawned()
    {
        EnsureController();
        EnsureWindowsArduino();

        if (controller == null)
        {
            Debug.LogError(
                $"[ArduinoDropDiscNetworkSync] {name}: " +
                "ArduinoController was not found in the network prefab hierarchy."
            );
            return;
        }

        Debug.Log(
            $"[ArduinoDropDiscNetworkSync] {name}: Spawned. " +
            $"HasStateAuthority={Object.HasStateAuthority}"
        );
    }


    public void RequestSpawnItemFromHardwarePeer()
    {
        if (!IsNetworkSpawned)
        {
            Debug.LogWarning(
                $"[ArduinoDropDiscNetworkSync] {name}: " +
                "Cannot request spawn because the NetworkObject is not spawned."
            );
            return;
        }

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
        {
            Debug.Log(
                $"[ArduinoDropDiscNetworkSync] {name}: " +
                "Cannot spawn item because the object is not spawned or does not have State Authority."
            );
            return;
        }

        if (!controller.TryGetItemSpawnData(
                out GameObject prefab,
                out Vector3 spawnPosition,
                out Quaternion spawnRotation))
        {
            Debug.LogError(
                $"[ArduinoDropDiscNetworkSync] {name}: " +
                "Failed to get item spawn data from ArduinoController."
            );
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

        Debug.Log(
            $"[ArduinoDropDiscNetworkSync] {name}: " +
            $"Spawned '{prefab.name}' at {spawnPosition} with rotation {spawnRotation}."
        );

        controller.RegisterNetworkSpawnedItem(spawnedObject.gameObject);
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

        Debug.Log(
            $"[ArduinoDropDiscNetworkSync] {name}: " +
            "Sweet collected on State Authority."
        );

        controller.HandleSweetCollectedOnStateAuthority(sweetObject);

        RPC_PlaySweetDropAnimation();

        SendSweetDropCommandToWindows();

        DespawnCollectedSweet(
            sweetObject,
            sweetNetworkObject
        );
    }


    private void SendSweetDropCommandToWindows()
    {
        EnsureWindowsArduino();

        if (windowsToHeadsetArduino == null)
        {
            Debug.LogError(
                $"[ArduinoDropDiscNetworkSync] {name}: " +
                "WindowsToHeadsetArduino could not be found."
            );
            return;
        }

        Debug.Log(
            $"[ArduinoDropDiscNetworkSync] {name}: " +
            "Sending Sweet drop command to Windows."
        );

        windowsToHeadsetArduino.SendSweetDropCommandFromHeadset();
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

        Destroy(sweetObject);
    }


    private void EnsureController()
    {
        if (controller == null)
        {
            controller = GetComponentInChildren<ArduinoController>(true);
        }
    }


    private void EnsureWindowsArduino()
    {
        if (windowsToHeadsetArduino == null)
        {
            windowsToHeadsetArduino =
                FindFirstObjectByType<WindowsToHeadsetArduino>();
        }
    }
}