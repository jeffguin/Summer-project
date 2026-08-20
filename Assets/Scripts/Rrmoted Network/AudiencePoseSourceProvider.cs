using UnityEngine;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using Valve.VR;
#endif

[DisallowMultipleComponent]
public sealed class AudiencePoseSourceProvider : MonoBehaviour
{
    [Header("Explicit Audience Sources")]
    [SerializeField] private DirectOpenVRTrackerReader headTrackerReader;
    [SerializeField] private Transform headSource;
    [SerializeField] private Transform rightHandSource;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private SteamVR_Behaviour_Pose rightHandPose;
#endif

    public string HeadSourceName =>
        headSource != null ? headSource.name : "None";

    public string RightHandSourceName =>
        rightHandSource != null ? rightHandSource.name : "None";

    private void Awake()
    {
        ResolveCachedReferences();
    }

    private void OnValidate()
    {
        ResolveCachedReferences();
    }

    public bool TryGetHeadPose(
        out Vector3 position,
        out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;

        if (headTrackerReader == null ||
            !headTrackerReader.isActiveAndEnabled ||
            !headTrackerReader.HasValidPose ||
            headSource == null ||
            !headSource.gameObject.activeInHierarchy)
        {
            return false;
        }

        position = headSource.position;
        rotation = headSource.rotation;
        return true;
    }

    public bool TryGetRightHandPose(
        out Vector3 position,
        out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;

        if (rightHandSource == null ||
            !rightHandSource.gameObject.activeInHierarchy)
        {
            return false;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (rightHandPose == null)
            rightHandPose =
                rightHandSource.GetComponent<SteamVR_Behaviour_Pose>();

        if (rightHandPose == null ||
            rightHandPose.poseAction == null ||
            !rightHandPose.isActive ||
            !rightHandPose.isValid ||
            !rightHandPose.poseAction[
                rightHandPose.inputSource
            ].deviceIsConnected)
        {
            return false;
        }

        position = rightHandSource.position;
        rotation = rightHandSource.rotation;
        return true;
#else
        return false;
#endif
    }

    private void ResolveCachedReferences()
    {
        if (headSource == null && headTrackerReader != null)
            headSource = headTrackerReader.Target;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        rightHandPose = rightHandSource != null
            ? rightHandSource.GetComponent<SteamVR_Behaviour_Pose>()
            : null;
#endif
    }
}
