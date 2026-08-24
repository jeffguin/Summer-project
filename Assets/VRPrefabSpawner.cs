using UnityEngine;

public class VRPrefabSpawner : MonoBehaviour
{
    [SerializeField] private GameObject prefabToSpawn;
    [SerializeField] private Transform spawnPoint;

 public void SpawnPrefab()
{
    if (prefabToSpawn == null)
    {
        Debug.LogWarning("No prefab assigned.");
        return;
    }

    if (spawnPoint == null)
    {
        Debug.LogWarning("No spawn point assigned.");
        return;
    }

    GameObject spawnedObject = Instantiate(
        prefabToSpawn,
        spawnPoint.position,
        spawnPoint.rotation
    );

    Rigidbody rb = spawnedObject.GetComponent<Rigidbody>();

    if (rb != null)
    {
        rb.useGravity = true;
        rb.isKinematic = false;

        Debug.Log(
            $"Spawned {spawnedObject.name} | " +
            $"Gravity: {rb.useGravity} | " +
            $"Kinematic: {rb.isKinematic}"
        );
    }
    else
    {
        Debug.LogWarning(
            $"Spawned {spawnedObject.name} has NO Rigidbody."
        );
    }
}
}