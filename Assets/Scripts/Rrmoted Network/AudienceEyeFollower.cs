using UnityEngine;

public class AudienceEyeFollower : MonoBehaviour
{
    [Header("Source")]
    public Transform trackerRaw;

    [Header("Calibration Offset")]
    public Vector3 positionOffset = Vector3.zero;

    [Header("Options")]
    public bool followRotation = false;

    private void LateUpdate()
    {
        if (trackerRaw == null)
            return;

        transform.position = trackerRaw.position + positionOffset;

        if (followRotation)
        {
            transform.rotation = trackerRaw.rotation;
        }
    }
}