using Fusion;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(NetworkObject))]
public sealed class ArduinoDropDiscNetworkSync : NetworkBehaviour
{
    [SerializeField] private ArduinoController controller;

    [SerializeField] private UnityEvent actorFunction;

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

        Debug.Log(
            $"[ArduinoDropDiscNetworkSync] {name}: Spawned. " +
            $"HasStateAuthority={Object.HasStateAuthority}"
        );
    }


    public void RequestActorFunctionFromHardwarePeer()
    {
        if (!IsNetworkSpawned)
        {
            Debug.LogWarning(
                "[ArduinoDropDiscNetworkSync] Cannot request actor function because the network object is not spawned."
            );
            return;
        }

        if (Object.HasStateAuthority)
        {
            actorFunction?.Invoke();
            return;
        }

        RPC_RequestActorFunctionFromHardwarePeer();
    }


    [Rpc(
        RpcSources.All,
        RpcTargets.StateAuthority,
        Channel = RpcChannel.Reliable)]
    private void RPC_RequestActorFunctionFromHardwarePeer()
    {
        actorFunction?.Invoke();
    }


    public void RequestSpawnItemFromHardwarePeer()
    {
        if (!IsNetworkSpawned)
        {
            Debug.LogWarning(
                "[ArduinoDropDiscNetworkSync] Cannot request spawn because the network object is not spawned."
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
    private void RPC_RequestSpawnItemFromHardwarePeer()
    {
        TrySpawnItemFromStateAuthority();
    }


    public void TrySpawnItemFromStateAuthority()
    {
        if (!CanRunAuthoritativeAction())
            return;

        EnsureController();

        if (controller == null)
        {
            Debug.LogError(
                "[ArduinoDropDiscNetworkSync] Cannot spawn because ArduinoController is missing."
            );
            return;
        }

        if (!controller.TryGetItemSpawnData(
            out GameObject prefab,
            out Vector3 spawnPosition,
            out Quaternion spawnRotation))
        {
            return;
        }

        NetworkObject prefabNetworkObject =
            prefab.GetComponent<NetworkObject>();

        if (prefabNetworkObject == null)
        {
            Debug.LogError(
                "[ArduinoDropDiscNetworkSync] Item prefab does not have a NetworkObject."
            );
            return;
        }

        NetworkObject spawnedNetworkObject = Runner.Spawn(
            prefabNetworkObject,
            spawnPosition,
            spawnRotation,
            inputAuthority: null
        );

        if (spawnedNetworkObject != null)
        {
            controller.RegisterNetworkSpawnedItem(
                spawnedNetworkObject.gameObject
            );
        }
    }


    public void TryCollectSweetFromStateAuthority(Collider other)
    {
        if (!CanRunAuthoritativeAction())
            return;

        if (other == null)
            return;

        EnsureController();

        if (controller == null)
        {
            Debug.LogError(
                "[ArduinoDropDiscNetworkSync] Cannot collect Sweet because ArduinoController is missing."
            );
            return;
        }

        GameObject sweetObject = other.gameObject;

        NetworkObject sweetNetworkObject =
            other.GetComponentInParent<NetworkObject>();

        if (sweetNetworkObject != null)
        {
            sweetObject = sweetNetworkObject.gameObject;
        }

        // controller.HandleSweetCollectedOnStateAuthority(
        //     sweetObject
        // );

        // RPC_HandleSweetCollectedForEveryone();

        // DespawnCollectedSweet(other);

        controller.HandleSweetCollectedOnStateAuthority(
            sweetObject
        );

        RPC_HandleSweetCollectedForEveryone();

        DespawnCollectedSweet(sweetNetworkObject);



    }


    [Rpc(
        RpcSources.StateAuthority,
        RpcTargets.All,
        Channel = RpcChannel.Reliable)]
    private void RPC_HandleSweetCollectedForEveryone()
    {
        EnsureController();

        if (controller != null)
        {
            controller.PlaySweetDropAnimationFromNetwork();
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        WindowsArduinoInput windowsArduino =
            FindFirstObjectByType<WindowsArduinoInput>();

        if (windowsArduino == null)
        {
            Debug.LogError(
                "[ArduinoDropDiscNetworkSync] WindowsArduinoInput was not found on Windows."
            );
            return;
        }

        Debug.Log(
            "[ArduinoDropDiscNetworkSync] Sweet collected. Sending 2 to Windows Arduino."
        );

        windowsArduino.SendSweetCollectedCommand();
#endif
    }


    private bool CanRunAuthoritativeAction()
    {
        if (!IsNetworkSpawned)
        {
            Debug.LogWarning(
                "[ArduinoDropDiscNetworkSync] Network object is not spawned."
            );
            return false;
        }

        if (!Object.HasStateAuthority)
        {
            Debug.LogWarning(
                "[ArduinoDropDiscNetworkSync] This peer does not have State Authority."
            );
            return false;
        }

        return true;
    }


    // private void DespawnCollectedSweet(Collider other)
    // {
    //     if (other == null)
    //         return;

    //     NetworkObject sweetNetworkObject =
    //         other.GetComponentInParent<NetworkObject>();

    //     if (sweetNetworkObject != null &&
    //         sweetNetworkObject != Object &&
    //         sweetNetworkObject.IsValid)
    //     {
    //         Runner.Despawn(sweetNetworkObject);
    //         return;
    //     }

    //     Destroy(other.gameObject);
    // }

    private void DespawnCollectedSweet(NetworkObject sweetNetworkObject)
{
    if (sweetNetworkObject == null ||
        !sweetNetworkObject.IsValid)
        return;

    Runner.Despawn(sweetNetworkObject);
}


    private void EnsureController()
    {
        if (controller != null)
            return;

        controller = GetComponentInChildren<ArduinoController>(true);

        if (controller == null)
        {
            controller = GetComponentInParent<ArduinoController>();
        }
    }
}