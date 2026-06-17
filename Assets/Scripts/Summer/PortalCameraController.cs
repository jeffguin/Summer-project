using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class PortalCameraController : MonoBehaviour
{
    [Header("Physical Setup")]
    public Transform screen;   // The PhysicalScreen Quad
    public Transform eye;      // The Tracked Eye / Head

    private Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
        // Reset matrices to ensure we start from a clean slate
        cam.ResetProjectionMatrix();
        cam.ResetWorldToCameraMatrix();
    }

    void LateUpdate()
    {
        if (screen == null || eye == null) return;

        UpdateCamera();
    }

    void UpdateCamera()
    {
        // --- 1. SCREEN CORNERS IN WORLD SPACE ---
        // Get the four corners of the 1x1 Unity Quad
        Vector3 bl = screen.TransformPoint(new Vector3(-0.5f, -0.5f, 0f));
        Vector3 br = screen.TransformPoint(new Vector3( 0.5f, -0.5f, 0f));
        Vector3 tl = screen.TransformPoint(new Vector3(-0.5f,  0.5f, 0f));

        // --- 2. SCREEN BASIS VECTORS ---
        Vector3 vr = (br - bl).normalized; // Right direction of screen
        Vector3 vu = (tl - bl).normalized; // Up direction of screen
        Vector3 vn = Vector3.Cross(vr, vu).normalized; // Normal (pointing away)

        Vector3 eyePos = eye.position;

        // Ensure the normal vector points TOWARD the eye for the math
        if (Vector3.Dot(vn, eyePos - bl) < 0f)
            vn = -vn;

        // --- 3. DISTANCE AND CLIP PLANES ---
        float near = cam.nearClipPlane;
        float far = cam.farClipPlane;

        // Perpendicular distance from eye to the screen plane
        float d = Vector3.Dot(eyePos - bl, vn);
        if (d < 0.01f) d = 0.01f;

        // --- 4. OFF-AXIS FRUSTUM BOUNDS ---
        // Project the vectors from eye to corners onto the screen axes
        float left   = Vector3.Dot(vr, bl - eyePos) * near / d;
        float right  = Vector3.Dot(vr, br - eyePos) * near / d;
        float bottom = Vector3.Dot(vu, bl - eyePos) * near / d;
        float top    = Vector3.Dot(vu, tl - eyePos) * near / d;

        // Apply the skewed projection matrix
        cam.projectionMatrix = Matrix4x4.Frustum(left, right, bottom, top, near, far);

        // --- 5. OBLIQUE NEAR CLIP PLANE ---
        // This slices objects perfectly at the screen surface
        Vector3 cameraSpaceNormal = cam.worldToCameraMatrix.MultiplyVector(vn);
        Vector3 cameraSpacePoint = cam.worldToCameraMatrix.MultiplyPoint(bl);
        float distanceToPlane = -Vector3.Dot(cameraSpaceNormal, cameraSpacePoint);
        
        // Create the plane vector (x, y, z, w)
        Vector4 clipPlane = new Vector4(cameraSpaceNormal.x, cameraSpaceNormal.y, cameraSpaceNormal.z, distanceToPlane);
        
        // Make the projection matrix respect the physical screen as the clipping point
        cam.projectionMatrix = cam.CalculateObliqueMatrix(clipPlane);

        // --- 6. CAMERA POSITION & ROTATION ---
        cam.transform.position = eyePos;
        // Look "into" the screen (-vn) while keeping screen-up (vu)
        cam.transform.rotation = Quaternion.LookRotation(-vn, vu); 
    }
}