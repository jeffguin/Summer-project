using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Transform))]
public class PhysicalWindowSize : MonoBehaviour
{
    [Header("Window Size (meters)")]
    //home default value 60cmx30cm
    public float width = 0.6f;  
    public float height = 0.3f; 

    void Update()
    {
        UpdateWindowScale();
    }

    void UpdateWindowScale()
    {
        transform.localScale = new Vector3(width, height, 1f);
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