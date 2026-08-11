using UnityEngine;
using static UnityEngine.Rendering.DebugUI;
using static UnityEngine.Rendering.DebugUI.Table;

public class HeadStablizer : MonoBehaviour
{
    public Transform rawHead;

    [Header("Thresholds")]
    //minimum position change that is considered "real"
    public float positionDeadband = 0.001f;    // 0.003 = 3 mm, If the raw head moves less than 3 mm from the filtered position, the filter ignores it.
    public float rotationDeadband = 1f;      // sam, but for rotation, 0.5 = 0.5 degrees

    [Header("Smoothing")]
    //how much the filtered value moves toward the raw value when the head is nearly stationary
    public float stillLerp = 1f;  // (very smooth) 0 --------- 1 (no smoothing)
    public float movingLerp = 0.5f; // (very smooth) 0 --------- 1 (no smoothing)

    [Header("Movement Detection")]
    //determines when the filter considers the head to be "moving." measured in m/s
    //How fast the head move before we stop treating it as jitter
    public float movementThreshold = 0.05f;    // 5cm/s
    // lower = move easier, higher = treat as still easier

    Vector3 filteredPos;
    Quaternion filteredRot;
    Vector3 previousRawPos;
    bool initialized;

    void LateUpdate()
    {
        if (rawHead == null) return;

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);

        if (!initialized)
        {
            filteredPos = rawHead.position;
            filteredRot = rawHead.rotation;
            previousRawPos = rawHead.position;
            initialized = true;
        }

        float speed = (rawHead.position - previousRawPos).magnitude / dt;

        float lerp = (speed < movementThreshold) ? stillLerp : movingLerp;

        // Position
        if (Vector3.Distance(filteredPos, rawHead.position) > positionDeadband)
            filteredPos = Vector3.Lerp(filteredPos, rawHead.position, lerp);

        // Rotation
        if (Quaternion.Angle(filteredRot, rawHead.rotation) > rotationDeadband)
            filteredRot = Quaternion.Slerp(filteredRot, rawHead.rotation, lerp);

        transform.SetPositionAndRotation(filteredPos, filteredRot);

        previousRawPos = rawHead.position;
    }
}