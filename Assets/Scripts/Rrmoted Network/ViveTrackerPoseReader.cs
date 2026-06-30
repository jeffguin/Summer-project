#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

using UnityEngine;
using Valve.VR;

public class ViveTrackerPoseReader : MonoBehaviour
{
    [Header("SteamVR Pose Action")]
    public SteamVR_Action_Pose poseAction;

    [Header("Tracker Input Source")]
    public SteamVR_Input_Sources inputSource = SteamVR_Input_Sources.Waist;

    [Header("Target Object To Move")]
    public Transform target;

    [Header("Debug")]
    public bool showDebugLog = true;

    private void Update()
    {
        if (poseAction == null)
        {
            if (showDebugLog)
                Debug.LogWarning("Pose Action is not assigned.");

            return;
        }

        if (target == null)
        {
            if (showDebugLog)
                Debug.LogWarning("Target is not assigned.");

            return;
        }

        bool isActive = poseAction[inputSource].active;
        bool isValid = poseAction[inputSource].poseIsValid;

        if (!isActive || !isValid)
        {
            if (showDebugLog)
            {
                Debug.LogWarning(
                    "Tracker pose is not active or not valid. " +
                    "Input Source: " + inputSource +
                    ", Active: " + isActive +
                    ", Valid: " + isValid
                );
            }

            return;
        }

        Vector3 position = poseAction[inputSource].localPosition;
        Quaternion rotation = poseAction[inputSource].localRotation;

        target.position = position;
        target.rotation = rotation;

        if (showDebugLog)
        {
            Debug.Log(
                "Tracker Pose: " +
                "Position = " + position.ToString("F3") +
                ", Rotation = " + rotation.eulerAngles.ToString("F1")
            );
        }
    }
}

#else

using UnityEngine;

public class ViveTrackerPoseReader : MonoBehaviour
{
    private void Awake()
    {
        Debug.LogWarning("ViveTrackerPoseReader is disabled on this platform. It only runs on Windows / Windows Editor with SteamVR.");
        enabled = false;
    }
}

#endif
