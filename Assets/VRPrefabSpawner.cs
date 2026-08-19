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

        Instantiate(
            prefabToSpawn,
            spawnPoint.position,
            spawnPoint.rotation
        );
    }
}