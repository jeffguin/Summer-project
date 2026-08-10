using UnityEngine;

public class EyeLocationSimulation : MonoBehaviour
{
    public Transform target;
    public Vector3 offset;   

    void LateUpdate()
    {
        if (target != null)
            transform.position = target.position + offset;
    }
}

