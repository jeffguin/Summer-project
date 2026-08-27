using UnityEngine;

public class PhysicalWindowSpawner : MonoBehaviour
{
    [Header("References")]

    [SerializeField]
    private GameObject quadPrefab;

    [SerializeField]
    private Transform viveTracker;


    [Header("Window Dimensions (meters)")]

    [SerializeField]
    private float width = 0.6f;

    [SerializeField]
    private float height = 0.3f;


    [Header("Tracker Position")]

    [Tooltip("Distance from the Vive Tracker to the centre of the quad's bottom edge.")]
    [SerializeField]
    private float trackerAboveBottom = 0.035f;


    [Header("Quad Rotation Correction")]

    [Tooltip("Additional rotation applied after calculating the quad orientation from the tracker.")]
    [SerializeField]
    private Vector3 rotationOffset;


    [Header("Keyboard Input")]

    [SerializeField]
    private KeyCode spawnKey = KeyCode.Space;


    private void Update()
    {
        if (Input.GetKeyDown(spawnKey))
        {
            SpawnWindow();
        }
    }


    public void SpawnWindow()
    {
        if (quadPrefab == null)
        {
            Debug.LogError(
                "PhysicalWindowSpawner: Quad Prefab is not assigned."
            );

            return;
        }


        if (viveTracker == null)
        {
            Debug.LogError(
                "PhysicalWindowSpawner: Vive Tracker is not assigned."
            );

            return;
        }



        // 1Calculate the desired bottom-centre position

        Vector3 desiredBottomCenter =
            viveTracker.position
            - Vector3.up * trackerAboveBottom;


  
        //  quad's forward direction
        // Tracker local +Y becomes the quad's forward direction.

        Vector3 quadForward =
            viveTracker.up;



        // Calculate  base quad rotation
        Quaternion baseRotation =
            Quaternion.LookRotation(
                quadForward,
                Vector3.up
            );

        //  Apply new rotation
        Quaternion finalRotation =
            baseRotation
            * Quaternion.Euler(rotationOffset);


   
        // Spawn the new window
        GameObject newWindow =
            Instantiate(
                quadPrefab
            );



        // apply target size

        WindowPrefabScript physicalWindow =
            newWindow.GetComponent<WindowPrefabScript>();


        if (physicalWindow != null)
        {
            physicalWindow.SetSize(
                width,
                height
            );
        }
        else
        {
            newWindow.transform.localScale =
                new Vector3(
                    width,
                    height,
                    1f
                );
        }


        // FINAL rotation
        newWindow.transform.rotation =
            finalRotation;


        // Find  quad's bottom-centre
        Vector3 bottomCenterLocal =
            new Vector3(
                0f,
                -0.5f,
                0f
            );


        Vector3 currentBottomCenter =
            newWindow.transform.TransformPoint(
                bottomCenterLocal
            );


        // lastly Position quad
        newWindow.transform.position +=
            desiredBottomCenter
            - currentBottomCenter;


        Debug.Log(
            $"Physical window spawned: {width}m x {height}m"
        );
    }
}