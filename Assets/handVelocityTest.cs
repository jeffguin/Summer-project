using UnityEngine;

public class ManualVelocityEstimator : MonoBehaviour
{
    private Rigidbody rb;
    private Vector3 previousPosition;
    private Quaternion previousRotation;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            previousPosition = rb.position;
            previousRotation = rb.rotation;
        }
    }

    void FixedUpdate()
    {
        if (rb == null) return;

        float deltaTime = Time.fixedDeltaTime;

        // 1. Calculate and set Linear Velocity (Speed)
        Vector3 linearVelocity = (rb.position - previousPosition) / deltaTime;
        rb.linearVelocity = linearVelocity;
        previousPosition = rb.position;

        // 2. Calculate and set Angular Velocity (Twist)
        Quaternion deltaRotation = rb.rotation * Quaternion.Inverse(previousRotation);
        deltaRotation.ToAngleAxis(out float angle, out Vector3 axis);

        // Convert angle to radians and divide by time to get angular velocity
        Vector3 angularVelocity = (angle * Mathf.Deg2Rad / deltaTime) * axis;
        rb.angularVelocity = angularVelocity;
        previousRotation = rb.rotation;
    }
}