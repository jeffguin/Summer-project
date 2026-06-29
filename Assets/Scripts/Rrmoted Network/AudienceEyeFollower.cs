using UnityEngine;
using UnityEngine.InputSystem;

public class AudienceEyeFollower : MonoBehaviour
{
    [Header("Source")]
    public Transform trackerRaw;

    [Header("Fish Tank Eye Start Position")]
    public Vector3 calibratedEyePosition = new Vector3(0f, 0f, -0.8f);

    [Header("Runtime State")]
    public Vector3 trackerStartPosition;
    public bool hasCalibrated = false;

    [Header("Options")]
    public bool calibrateOnStart = true;
    public bool followRotation = false;

    private void Start()
    {
        if (calibrateOnStart)
        {
            CalibrateTrackerStart();
        }
    }

    private void LateUpdate()
    {
        if (trackerRaw == null)
        {
            Debug.LogWarning("AudienceEyeFollower: TrackerRaw is not assigned.");
            return;
        }

        if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
        {
            CalibrateTrackerStart();
        }

        if (!hasCalibrated)
        {
            return;
        }

        Vector3 trackerDelta = trackerRaw.position - trackerStartPosition;

        transform.position = calibratedEyePosition + trackerDelta;

        if (followRotation)
        {
            transform.rotation = trackerRaw.rotation;
        }
    }

    [ContextMenu("Calibrate Tracker Start")]
    public void CalibrateTrackerStart()
    {
        if (trackerRaw == null)
        {
            Debug.LogWarning("AudienceEyeFollower: Cannot calibrate because TrackerRaw is not assigned.");
            return;
        }

        trackerStartPosition = trackerRaw.position;
        hasCalibrated = true;

        transform.position = calibratedEyePosition;

        Debug.Log(
            "Tracker start calibrated." +
            "\nTracker Start Position: " + trackerStartPosition.ToString("F3") +
            "\nAudienceEye Start Position: " + calibratedEyePosition.ToString("F3")
        );
    }
}