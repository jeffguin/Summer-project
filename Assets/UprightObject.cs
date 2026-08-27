using UnityEngine;

public class UprightObject : MonoBehaviour
{
    [SerializeField] private float verticalOffset = 0.3f;

    private void LateUpdate()
    {
        if (transform.parent == null)
            return;

        // Keep the body directly below the head
        transform.position = transform.parent.position + Vector3.down * verticalOffset;

        // Keep the body upright
        transform.rotation = Quaternion.identity;
    }
}