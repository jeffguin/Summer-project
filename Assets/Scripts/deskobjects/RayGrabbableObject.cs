using UnityEngine;

public class RayGrabbableObject : MonoBehaviour
{
    [Header("Grab Settings")]
    [SerializeField] private bool allowGrab = true;
    [SerializeField] private bool useRigidbody = true;

    private Rigidbody rb;
    private bool originalUseGravity;
    private bool originalIsKinematic;

    public bool AllowGrab => allowGrab;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    public void OnGrabBegin()
    {
        if (!allowGrab)
            return;

        if (useRigidbody && rb != null)
        {
            originalUseGravity = rb.useGravity;
            originalIsKinematic = rb.isKinematic;

            rb.useGravity = false;
            rb.isKinematic = true;
        }

        Debug.Log("RayGrabbableObject: Grab begin - " + gameObject.name);
    }

    public void OnGrabMove(Vector3 targetPosition, Quaternion targetRotation, bool applyRotation)
    {
        if (!allowGrab)
            return;

        if (useRigidbody && rb != null)
        {
            rb.MovePosition(targetPosition);

            if (applyRotation)
                rb.MoveRotation(targetRotation);
        }
        else
        {
            transform.position = targetPosition;

            if (applyRotation)
                transform.rotation = targetRotation;
        }
    }

    public void OnGrabEnd()
    {
        if (!allowGrab)
            return;

        if (useRigidbody && rb != null)
        {
            rb.useGravity = originalUseGravity;
            rb.isKinematic = originalIsKinematic;
        }

        Debug.Log("RayGrabbableObject: Grab end - " + gameObject.name);
    }
}

