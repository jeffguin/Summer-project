using UnityEngine;

public class UprightObject : MonoBehaviour
{
    private void LateUpdate()
    {
        Vector3 forward = transform.parent.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }
    }
}