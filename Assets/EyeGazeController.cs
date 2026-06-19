using UnityEngine;

public class EyeGazeController : MonoBehaviour
{
    [Header("Eye Transforms")]
    public Transform leftEye;
    public Transform rightEye;

    [Header("Settings")]
    public float maxYaw = 25f;
    public float maxPitch = 20f;
    public float smoothSpeed = 10f;

    [Header("Input (set from tracking system, don't touch)")]
    [Range(-1f, 1f)] public float gazeX; // left-right
    [Range(-1f, 1f)] public float gazeY; // up-down

    private Quaternion leftTarget;
    private Quaternion rightTarget;

    void Update()
    {
        // Clamp input (stability)
        float x = Mathf.Clamp(gazeX, -1f, 1f);
        float y = Mathf.Clamp(gazeY, -1f, 1f);

        // Convert gaze to rotation
        float yaw = x * maxYaw;
        float pitch = -y * maxPitch;

        Quaternion targetRotation = Quaternion.Euler(pitch, yaw, 0f);

        // Smooth both eyes
        leftTarget = Quaternion.Slerp(leftTarget, targetRotation, Time.deltaTime * smoothSpeed);
        rightTarget = Quaternion.Slerp(rightTarget, targetRotation, Time.deltaTime * smoothSpeed);

        if (leftEye != null)
            leftEye.localRotation = leftTarget;

        if (rightEye != null)
            rightEye.localRotation = rightTarget;
    }
}