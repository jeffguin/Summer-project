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


        // --------------------------------------------------
        // 1. Calculate the desired bottom-centre position
        // --------------------------------------------------

        // The Vive Tracker is positioned 3.5 cm above
        // the centre of the quad's bottom edge.

        Vector3 desiredBottomCenter =
            viveTracker.position
            - Vector3.up * trackerAboveBottom;


        // --------------------------------------------------
        // 2. Calculate the quad's rotation
        // --------------------------------------------------

        // Vive Tracker local +Y becomes the quad's forward direction.

        Vector3 quadForward =
            viveTracker.up;


        // Keep the quad's up direction aligned with world up.

        Vector3 quadUp =
            Vector3.up;


        Quaternion quadRotation =
            Quaternion.LookRotation(
                quadForward,
                quadUp
            );


        // --------------------------------------------------
        // 3. Spawn the new window
        // --------------------------------------------------

        GameObject newWindow =
            Instantiate(
                quadPrefab
            );


        // --------------------------------------------------
        // 4. Apply the desired size
        // --------------------------------------------------

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
            Debug.LogWarning(
                "PhysicalWindowSpawner: WindowPrefabScript is missing from the prefab. Applying scale directly."
            );

            newWindow.transform.localScale =
                new Vector3(
                    width,
                    height,
                    1f
                );
        }


        // --------------------------------------------------
        // 5. Apply the desired rotation
        // --------------------------------------------------

        newWindow.transform.rotation =
            quadRotation;


        // --------------------------------------------------
        // 6. Find the quad's bottom-centre
        // --------------------------------------------------

        // A standard Unity Quad is 1 x 1 metres
        // and its origin is at its centre.

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


        // --------------------------------------------------
        // 7. Move the quad so its bottom-centre is
        //    exactly 3.5 cm below the Vive Tracker
        // --------------------------------------------------

        newWindow.transform.position +=
            desiredBottomCenter
            - currentBottomCenter;


        Debug.Log(
            $"Physical window spawned: {width}m x {height}m"
        );
    }
}