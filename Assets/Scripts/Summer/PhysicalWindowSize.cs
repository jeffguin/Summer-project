using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Transform))]
public class PhysicalWindowSize : MonoBehaviour
{
    [Header("Window Size (meters)")]
    // jeffrey home pc screen default value: 60cm x 30cm
    public float width = 0.6f;
    public float height = 0.3f;

    [Header("Ground Placement")]
    public GameObject ground;

    [Tooltip("Distance between the lowest point of the quad and the ground.")]
    public float heightAboveGround = 0.1f;

    private Collider groundCollider;

    void Update()
    {
        UpdateWindowScale();
        UpdateWindowHeight();
    }

    void UpdateWindowScale()
    {
        transform.localScale = new Vector3(width, height, 1f);
    }

    void UpdateWindowHeight()
    {
        if (ground == null)
            return;

        groundCollider = ground.GetComponent<Collider>();

        if (groundCollider == null)
        {
            Debug.LogWarning("Ground object does not have a Collider.");
            return;
        }

        // Get the lowest point of the quad in world space
        Vector3 bottomLeft = GetBottomLeft();
        Vector3 bottomRight = GetBottomRight();

        float lowestY = Mathf.Min(bottomLeft.y, bottomRight.y);

        // Get the top surface of the ground
        float groundTopY = groundCollider.bounds.max.y;

        // Move the entire window so its lowest point is the desired distance above ground
        float requiredY = groundTopY + heightAboveGround;
        float offsetY = requiredY - lowestY;

        transform.position += new Vector3(0f, offsetY, 0f);
    }

    public Vector3 GetBottomLeft()
    {
        return transform.TransformPoint(new Vector3(-0.5f, -0.5f, 0));
    }

    public Vector3 GetBottomRight()
    {
        return transform.TransformPoint(new Vector3(0.5f, -0.5f, 0));
    }

    public Vector3 GetTopLeft()
    {
        return transform.TransformPoint(new Vector3(-0.5f, 0.5f, 0));
    }

    public Vector3 GetTopRight()
    {
        return transform.TransformPoint(new Vector3(0.5f, 0.5f, 0));
    }
}