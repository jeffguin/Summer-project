using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class PortalCameraController : MonoBehaviour
{
    [Header("Physical Setup")]
    public Transform screen;   // The PhysicalScreen Quad
    public Transform eye;      // The Tracked Eye / Head

    [Header("Adaptive FOV Limiting")]
    [Tooltip("Distance from the physical screen where FOV limiting begins.")]
    [Min(0.01f)]
    public float fovLimitStartDistance = 1.0f;

    [Tooltip("Distance over which the FOV smoothly transitions from normal to the maximum FOV.")]
    [Min(0.001f)]
    public float transitionDistance = 0.3f;

    [Tooltip("Maximum horizontal FOV allowed when the audience is close to the screen.")]
    [Range(1f, 179f)]
    public float maximumHorizontalFOV = 80f;

    [Tooltip("Maximum vertical FOV allowed when the audience is close to the screen.")]
    [Range(1f, 179f)]
    public float maximumVerticalFOV = 60f;

    private Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();

        // Reset matrices to ensure always starting from a clean slate
        cam.ResetProjectionMatrix();
        cam.ResetWorldToCameraMatrix();
    }

    void LateUpdate()
    {
        if (screen == null || eye == null)
            return;

        UpdateCamera();
    }

    void UpdateCamera()
    {

        // Get the four corners of the 1x1 Unity Quad
        Vector3 bl = screen.TransformPoint(
            new Vector3(-0.5f, -0.5f, 0f)
        );

        Vector3 br = screen.TransformPoint(
            new Vector3(0.5f, -0.5f, 0f)
        );

        Vector3 tl = screen.TransformPoint(
            new Vector3(-0.5f, 0.5f, 0f)
        );



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
        float d = Vector3.Dot(
            eyePos - bl,
            vn
        );

        d = Mathf.Max(d, 0.01f);


        // NORMAL OFF-AXIS FRUSTUM


        float left =
            Vector3.Dot(vr, bl - eyePos)
            * near / d;

        float right =
            Vector3.Dot(vr, br - eyePos)
            * near / d;

        float bottom =
            Vector3.Dot(vu, bl - eyePos)
            * near / d;

        float top =
            Vector3.Dot(vu, tl - eyePos)
            * near / d;


        // Flexible FOV limits
   

        ApplyAdaptiveFOVLimit(
            ref left,
            ref right,
            ref bottom,
            ref top,
            near,
            d
        );

  


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
       

        // Calculate the physical screen plane
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

        // --------------------------------------------------
        // 8. CAMERA POSITION & ROTATION
        // --------------------------------------------------

        cam.transform.position = eyePos;

        // Look into the screen while keeping screen-up
        cam.transform.rotation =
            Quaternion.LookRotation(
                -vn,
                vu
            );
    }

    private void ApplyAdaptiveFOVLimit(
        ref float left,
        ref float right,
        ref float bottom,
        ref float top,
        float near,
        float distance
    )
    {
        // --------------------------------------------------
        // Calculate how much FOV limiting should be applied
        // --------------------------------------------------

        float transitionStart =
            fovLimitStartDistance;

        float transitionEnd =
            Mathf.Max(
                0.01f,
                fovLimitStartDistance - transitionDistance
            );

        // Farther than the start distance:
        // no FOV limitation.
        if (distance >= transitionStart)
            return;

        // Calculate transition amount.
        //
        // 0 = normal FOV
        // 1 = maximum FOV limitation
        float t = Mathf.InverseLerp(
            transitionStart,
            transitionEnd,
            distance
        );

        // Smooth the transition so there is no sudden change.
        t = Mathf.SmoothStep(
            0f,
            1f,
            t
        );

        // --------------------------------------------------
        // Current FOV
        // --------------------------------------------------

        float currentHorizontalFOV =
            Mathf.Atan2(right, near) -
            Mathf.Atan2(left, near);

        currentHorizontalFOV *= Mathf.Rad2Deg;

        float currentVerticalFOV =
            Mathf.Atan2(top, near) -
            Mathf.Atan2(bottom, near);

        currentVerticalFOV *= Mathf.Rad2Deg;

        // --------------------------------------------------
        // Calculate target FOV
        // --------------------------------------------------

        float targetHorizontalFOV =
            Mathf.Min(
                currentHorizontalFOV,
                maximumHorizontalFOV
            );

        float targetVerticalFOV =
            Mathf.Min(
                currentVerticalFOV,
                maximumVerticalFOV
            );

        // Interpolate between normal and limited FOV.
        float desiredHorizontalFOV =
            Mathf.Lerp(
                currentHorizontalFOV,
                targetHorizontalFOV,
                t
            );

        float desiredVerticalFOV =
            Mathf.Lerp(
                currentVerticalFOV,
                targetVerticalFOV,
                t
            );

        // --------------------------------------------------
        // Scale the frustum around its CURRENT centre
        //
        // This is important for off-axis projection.
        // We don't recenter the projection.
        // --------------------------------------------------

        float horizontalScale =
            Mathf.Tan(
                desiredHorizontalFOV *
                0.5f *
                Mathf.Deg2Rad
            )
            /
            Mathf.Tan(
                currentHorizontalFOV *
                0.5f *
                Mathf.Deg2Rad
            );

        float verticalScale =
            Mathf.Tan(
                desiredVerticalFOV *
                0.5f *
                Mathf.Deg2Rad
            )
            /
            Mathf.Tan(
                currentVerticalFOV *
                0.5f *
                Mathf.Deg2Rad
            );

        // Horizontal centre of the existing
        // off-axis frustum.
        float horizontalCentre =
            (left + right) * 0.5f;

        float horizontalHalfWidth =
            (right - left) * 0.5f;

        horizontalHalfWidth *= horizontalScale;

        left =
            horizontalCentre -
            horizontalHalfWidth;

        right =
            horizontalCentre +
            horizontalHalfWidth;

        // Vertical centre of the existing
        // off-axis frustum.
        float verticalCentre =
            (bottom + top) * 0.5f;

        float verticalHalfHeight =
            (top - bottom) * 0.5f;

        verticalHalfHeight *= verticalScale;

        bottom =
            verticalCentre -
            verticalHalfHeight;

        top =
            verticalCentre +
            verticalHalfHeight;
    }
}