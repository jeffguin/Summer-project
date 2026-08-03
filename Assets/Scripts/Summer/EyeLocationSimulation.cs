using UnityEngine;
using Valve.VR;

public class EyeLocationSimulation : MonoBehaviour
{
    public Transform target;
    public Vector3 offset;


    /// /////////

    void Start()
    {
        SteamVR_Behaviour_Pose[] trackers =
            FindObjectsByType<SteamVR_Behaviour_Pose>(
                FindObjectsSortMode.None);

        foreach (var tracker in trackers)
        {
            Debug.Log(tracker.gameObject.name);
        }
    }

    /// /////////////
 





    void LateUpdate()
    {
        if (target != null)
            transform.position = target.position + offset;
    }
}


