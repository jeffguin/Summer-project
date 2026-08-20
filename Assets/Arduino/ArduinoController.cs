using UnityEngine;
using System.Collections;

public class ArduinoController : MonoBehaviour
{
    [Header("TicTacToe Object Spawning")]
    [SerializeField] private GameObject itemIntoVirtualPrefab;
    [SerializeField] private Transform spawnPoint;

    private GameObject spawnedItem;

    private ArduinoDropDiscNetworkSync networkSync;


    [Header("Sweet/Medal Detection, Destroy and Play Animation")]
    [SerializeField] private Collider TriggerCollider;
    [SerializeField] private Animator discAnimator;


    [Header("Animation")]
    [SerializeField] private string fallTriggerName = "Fall";


    [Header("Rotation")]
    public bool canRotate = false;

    [Header("Into Virtual Cooldown")]
    [SerializeField] private float intoVirtualCooldown = 10f;
    [SerializeField] private bool canIntoVirtual = true;


    private void Awake()
    {
        networkSync = GetComponentInParent<ArduinoDropDiscNetworkSync>();
    }


    private void Start()
    {
        if (networkSync != null)
            return;
    }


    public void ArduinoButtonPressed()
    {
        if (!canIntoVirtual)
        {
            Debug.Log("Sweet drop is on cooldown.");
            return;
        }

        canIntoVirtual = false;

        Debug.Log("Arduino button pressed.");

        StartCoroutine(IntoVirtualCooldown());

        if (networkSync != null && networkSync.IsNetworkSpawned)
        {
            Debug.Log("Requesting NETWORK Sweet spawn.");

            networkSync.RequestSpawnItemFromHardwarePeer();
            return;
        }

        Debug.Log("Spawning LOCAL Sweet.");

        SpawnItem();
    }


    private IEnumerator IntoVirtualCooldown()
    {
        yield return new WaitForSeconds(intoVirtualCooldown);
        canIntoVirtual = true;
        Debug.Log("Into Virtual cooldown finished.");
    }


    private void SpawnItem()
    {
        if (itemIntoVirtualPrefab == null)
        {
            Debug.LogError("Item Into Virtual Prefab has not been assigned.");
            return;
        }

        if (spawnedItem != null)
        {
            Debug.LogWarning("An Item_Into_Virtual is already spawned.");
            return;
        }

        Vector3 spawnPosition = transform.position;
        Quaternion spawnRotation = transform.rotation;

        if (spawnPoint != null)
        {
            spawnPosition = spawnPoint.position;
            spawnRotation = spawnPoint.rotation;
        }

        spawnedItem = Instantiate(
            itemIntoVirtualPrefab,
            spawnPosition,
            spawnRotation
        );

        Debug.Log("Item_Into_Virtual spawned.");
    }


    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Sweet"))
            return;

        if (networkSync != null && networkSync.IsNetworkSpawned)
        {
            networkSync.TryCollectSweetFromStateAuthority(other);
            return;
        }

        HandleSweetCollectedLocally(other);
    }


    private void HandleSweetCollectedLocally(Collider other)
    {
        Debug.Log("Sweet object entered the trigger.");

        PlayFallingAnimation();

        Destroy(other.gameObject);

        if (other.gameObject == spawnedItem)
        {
            spawnedItem = null;
        }
    }


    internal void HandleSweetCollectedOnStateAuthority(GameObject sweetObject)
    {
        Debug.Log("Sweet object entered the network-authoritative trigger.");

        ClearSpawnedItemIfMatches(sweetObject);
    }


    internal void PlayFallingAnimationFromNetwork()
    {
        PlayFallingAnimation();
    }


    internal void PlaySweetDropAnimationFromNetwork()
    {
        PlayFallingAnimation();
    }


    internal bool TryGetItemSpawnData(
        out GameObject prefab,
        out Vector3 spawnPosition,
        out Quaternion spawnRotation)
    {
        prefab = itemIntoVirtualPrefab;
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;

        if (prefab == null)
        {
            Debug.LogError("Item Into Virtual Prefab has not been assigned.");
            return false;
        }

        if (spawnPoint != null)
        {
            spawnPosition = spawnPoint.position;
            spawnRotation = spawnPoint.rotation;
        }

        return true;
    }


    internal void RegisterNetworkSpawnedItem(GameObject item)
    {
        spawnedItem = item;

        Debug.Log("Item_Into_Virtual network-spawned.");
    }


    private void ClearSpawnedItemIfMatches(GameObject sweetObject)
    {
        if (spawnedItem == null || sweetObject == null)
            return;

        if (sweetObject == spawnedItem ||
            sweetObject.transform.IsChildOf(spawnedItem.transform))
        {
            spawnedItem = null;
        }
    }


    private void PlayFallingAnimation()
    {
        if (discAnimator == null)
        {
            Debug.LogError("Disc Animator has not been assigned.");
            return;
        }

        discAnimator.SetTrigger(fallTriggerName);

        Debug.Log("FallingDisc animation triggered.");
    }
}