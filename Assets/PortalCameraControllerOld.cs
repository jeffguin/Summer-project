using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class PortalCameraControllerOld : MonoBehaviour
{
    [Header("Physical Setup")]
    public Transform screen;   // The PhysicalScreen Quad
    public Transform eye;      // The Tracked Eye / Head

    private Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
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
        // SCREEN CORNERS IN WORLD SPACE 
        // Get the four corners of the 1x1 Unity Quad
        Vector3 bl = screen.TransformPoint(new Vector3(-0.5f, -0.5f, 0f));
        Vector3 br = screen.TransformPoint(new Vector3(0.5f, -0.5f, 0f));
        Vector3 tl = screen.TransformPoint(new Vector3(-0.5f, 0.5f, 0f));

        // SCREEN BASIS VECTORS
        Vector3 vr = (br - bl).normalized;
        Vector3 vu = (tl - bl).normalized;
        Vector3 vn = Vector3.Cross(vr, vu).normalized;

        Vector3 eyePos = eye.position;

        // Ensure the normal vector points TOWARD the eye
        if (Vector3.Dot(vn, eyePos - bl) < 0f)
            vn = -vn;

        // DISTANCE AND CLIP PLANES
        float near = cam.nearClipPlane;
        float far = cam.farClipPlane;

        // Perpendicular distance from eye to the screen plane
        float d = Vector3.Dot(eyePos - bl, vn);

        if (d < 0.01f)
            d = 0.01f;

        // OFF-AXIS FRUSTUM BOUNDS
        float left =
            Vector3.Dot(vr, bl - eyePos) * near / d;

        float right =
            Vector3.Dot(vr, br - eyePos) * near / d;

        float bottom =
            Vector3.Dot(vu, bl - eyePos) * near / d;

        float top =
            Vector3.Dot(vu, tl - eyePos) * near / d;

        // Apply the skewed projection matrix
        cam.projectionMatrix =
            Matrix4x4.Frustum(
                left,
                right,
                bottom,
                top,
                near,
                far
            );

        // OBLIQUE NEAR CLIP PLANE
        Vector3 cameraSpaceNormal =
            cam.worldToCameraMatrix.MultiplyVector(vn);

        Vector3 cameraSpacePoint =
            cam.worldToCameraMatrix.MultiplyPoint(bl);

        float distanceToPlane =
            -Vector3.Dot(
                cameraSpaceNormal,
                cameraSpacePoint
            );

        // Create the plane vector
        Vector4 clipPlane = new Vector4(
            cameraSpaceNormal.x,
            cameraSpaceNormal.y,
            cameraSpaceNormal.z,
            distanceToPlane
        );

        // Make the projection matrix respect the physical screen
        cam.projectionMatrix =
            cam.CalculateObliqueMatrix(clipPlane);

        // CAMERA POSITION & ROTATION
        cam.transform.position = eyePos;

        // Look "into" the screen (-vn)
        // while keeping screen-up (vu)
        cam.transform.rotation =
            Quaternion.LookRotation(-vn, vu);
    }
}