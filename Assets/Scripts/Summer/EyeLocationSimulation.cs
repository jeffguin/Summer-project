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




//To use on UI: call SwitchTracker() with button


//using UnityEngine;

//public class EyeLocationSimulation : MonoBehaviour
//{
//    [Header("Trackers")]
//    public Transform trackerH1;
//    public Transform trackerH2;

//    [Header("Current Target")]
//    public Transform target;

//    public Vector3 offset;

//    void Start()
//    {
//        if (trackerH1 == null)
//            trackerH1 = GameObject.Find("H1")?.transform;

//        if (trackerH2 == null)
//            trackerH2 = GameObject.Find("H2")?.transform;

//        if (target == null)
//            target = trackerH1;
//    }

//    void LateUpdate()
//    {
//        if (target != null)
//            transform.position = target.position + offset;
//    }

//    public void SwitchTracker()
//    {
//        if (target == trackerH1)
//            target = trackerH2;
//        else
//            target = trackerH1;
//    }
//}

