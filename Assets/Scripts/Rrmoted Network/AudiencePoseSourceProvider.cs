using UnityEngine;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using Valve.VR;
#endif

[DisallowMultipleComponent]
public sealed class AudiencePoseSourceProvider : MonoBehaviour
{
    [Header("Network Source Selection")]
    [Tooltip("用于日志和 Inspector 识别该姿态来源，例如 H1。")]
    [SerializeField] private string networkSourceLabel = "Audience";

    [Tooltip("启用后，此来源才会向演员端发送观众头部和右手姿态。一个观众场景只应启用一个。")]
    [SerializeField] private bool useForNetworkPose = true;

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

    public string NetworkSourceLabel =>
        string.IsNullOrWhiteSpace(networkSourceLabel)
            ? gameObject.name
            : networkSourceLabel;

    public bool UseForNetworkPose => useForNetworkPose;

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
