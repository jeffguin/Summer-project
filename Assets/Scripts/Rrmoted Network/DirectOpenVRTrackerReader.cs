using UnityEngine;
using Valve.VR;

public class DirectOpenVRTrackerReader : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Tracker Selection")]
    [Tooltip("0 = first Vive Tracker found, 1 = second tracker, etc.")]
    public int trackerNumber = 0;

    [Header("Optional Tracking Origin")]
    public Transform trackingOrigin;

    [Header("Debug")]
    public bool showDebugLog = true;

    private TrackedDevicePose_t[] poses =
        new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

    private uint currentTrackerIndex = OpenVR.k_unTrackedDeviceIndexInvalid;
    private float logTimer = 0f;

    private void Update()
    {
        if (OpenVR.System == null)
        {
            LogOncePerSecond("OpenVR.System is null. Is SteamVR running?");
            return;
        }

        if (target == null)
        {
            LogOncePerSecond("Target is not assigned.");
            return;
        }

        currentTrackerIndex = FindTrackerIndex(trackerNumber);

        if (currentTrackerIndex == OpenVR.k_unTrackedDeviceIndexInvalid)
        {
            LogOncePerSecond("No Vive Tracker found. Check SteamVR device status.");
            return;
        }

        OpenVR.System.GetDeviceToAbsoluteTrackingPose(
            ETrackingUniverseOrigin.TrackingUniverseStanding,
            0,
            poses
        );

        TrackedDevicePose_t pose = poses[currentTrackerIndex];

        if (!pose.bDeviceIsConnected || !pose.bPoseIsValid)
        {
            LogOncePerSecond(
                "Tracker found but pose is invalid. " +
                "Index: " + currentTrackerIndex +
                ", Connected: " + pose.bDeviceIsConnected +
                ", Valid: " + pose.bPoseIsValid
            );
            return;
        }

        SteamVR_Utils.RigidTransform rigidTransform =
            new SteamVR_Utils.RigidTransform(pose.mDeviceToAbsoluteTracking);

        if (trackingOrigin != null)
        {
            target.position = trackingOrigin.TransformPoint(rigidTransform.pos);
            target.rotation = trackingOrigin.rotation * rigidTransform.rot;
        }
        else
        {
            target.position = rigidTransform.pos;
            target.rotation = rigidTransform.rot;
        }

        LogOncePerSecond(
            "Tracker OK. " +
            "Index: " + currentTrackerIndex +
            ", Position: " + target.position.ToString("F3") +
            ", Rotation: " + target.rotation.eulerAngles.ToString("F1")
        );
    }

    private uint FindTrackerIndex(int targetTrackerNumber)
    {
        int foundCount = 0;

        for (uint i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
        {
            if (!OpenVR.System.IsTrackedDeviceConnected(i))
                continue;

            ETrackedDeviceClass deviceClass =
                OpenVR.System.GetTrackedDeviceClass(i);

            if (deviceClass == ETrackedDeviceClass.GenericTracker)
            {
                if (foundCount == targetTrackerNumber)
                    return i;

                foundCount++;
            }
        }

        return OpenVR.k_unTrackedDeviceIndexInvalid;
    }

    private void LogOncePerSecond(string message)
    {
        if (!showDebugLog)
            return;

        logTimer += Time.deltaTime;

        if (logTimer >= 1f)
        {
            logTimer = 0f;
            Debug.Log(message);
        }
    }
}