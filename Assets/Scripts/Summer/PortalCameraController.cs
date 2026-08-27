using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class PortalCameraController : MonoBehaviour
{
    [Header("Physical Setup")]
    public Transform screen;   // Physical Screen Quad
    public Transform eye;      // Tracked Eye / Simulated Eye

    [Header("Distance-Based Object Size")]
    [Tooltip("Eye-to-screen distance at which the projection has its normal size.")]
    public float referenceEyeDistance = 1.0f;

    [Tooltip("0 = disabled, 1 = full distance-based perspective scaling.")]
    [Range(0f, 1f)]
    public float distanceSizeStrength = 1.0f;

    [Tooltip("Minimum multiplier applied to the projection.")]
    public float minimumSizeMultiplier = 0.5f;

    [Tooltip("Maximum multiplier applied to the projection.")]
    public float maximumSizeMultiplier = 2.0f;

    private Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();

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


        Vector3 bl = screen.TransformPoint(new Vector3(-0.5f, -0.5f, 0f));
        Vector3 br = screen.TransformPoint(new Vector3(0.5f, -0.5f, 0f));
        Vector3 tl = screen.TransformPoint(new Vector3(-0.5f, 0.5f, 0f));


        Vector3 vr = (br - bl).normalized;
        Vector3 vu = (tl - bl).normalized;
        Vector3 vn = Vector3.Cross(vr, vu).normalized;

        Vector3 eyePos = eye.position;

        // Make the normal point toward the eye.
        if (Vector3.Dot(vn, eyePos - bl) < 0f)
            vn = -vn;



        cam.transform.position = eyePos;
        cam.transform.rotation = Quaternion.LookRotation(-vn, vu);


        //  EYE-TO-SCREEN DISTANCE

        float eyeDistance = Vector3.Dot(eyePos - bl, vn);

        eyeDistance = Mathf.Max(eyeDistance, 0.01f);


        //nORMAL OFF-AXIS FRUSTUM


        float near = cam.nearClipPlane;
        float far = cam.farClipPlane;

        float left =
            Vector3.Dot(vr, bl - eyePos) * near / eyeDistance;

        float right =
            Vector3.Dot(vr, br - eyePos) * near / eyeDistance;

        float bottom =
            Vector3.Dot(vu, bl - eyePos) * near / eyeDistance;

        float top =
            Vector3.Dot(vu, tl - eyePos) * near / eyeDistance;

        // =========================================================
        //  DISTANCE-BASED SIZE CONTROL
        //
        // Reference distance:
        //
        //     eyeDistance == referenceEyeDistance
        //
        // gives multiplier = 1.
        //
        // Closer:
        //
        //     eyeDistance < reference
        //
        // gives multiplier > 1
        //
        // Farther:
        //
        //     eyeDistance > reference
        //
        // gives multiplier < 1
        //
        // This makes virtual objects:
        //
        //     closer  -> larger
        //     farther -> smaller
        //
        // =========================================================

        float distanceMultiplier =
            referenceEyeDistance / eyeDistance;

        distanceMultiplier = Mathf.Lerp(
            1.0f,
            distanceMultiplier,
            distanceSizeStrength
        );

        distanceMultiplier = Mathf.Clamp(
            distanceMultiplier,
            minimumSizeMultiplier,
            maximumSizeMultiplier
        );

        // Scale the frustum around its centre.
        //
        // Smaller frustum = larger objects.
        // Larger frustum  = smaller objects.
        //
        // We therefore invert the multiplier here.

        float projectionScale = 1.0f / distanceMultiplier;

        float frustumCenterX = (left + right) * 0.5f;
        float frustumCenterY = (bottom + top) * 0.5f;

        float halfWidth = (right - left) * 0.5f;
        float halfHeight = (top - bottom) * 0.5f;

        halfWidth *= projectionScale;
        halfHeight *= projectionScale;

        left = frustumCenterX - halfWidth;
        right = frustumCenterX + halfWidth;

        bottom = frustumCenterY - halfHeight;
        top = frustumCenterY + halfHeight;


        // OFF-AXIS PROJECTION


        cam.projectionMatrix =
            Matrix4x4.Frustum(
                left,
                right,
                bottom,
                top,
                near,
                far
            );


        // OBLIQUE NEAR CLIPPING


        Vector3 cameraSpaceNormal =
            cam.worldToCameraMatrix.MultiplyVector(vn);

        Vector3 cameraSpacePoint =
            cam.worldToCameraMatrix.MultiplyPoint(bl);

        float distanceToPlane =
            -Vector3.Dot(
                cameraSpaceNormal,
                cameraSpacePoint
            );

        Vector4 clipPlane = new Vector4(
            cameraSpaceNormal.x,
            cameraSpaceNormal.y,
            cameraSpaceNormal.z,
            distanceToPlane
        );

        cam.projectionMatrix =
            cam.CalculateObliqueMatrix(clipPlane);
    }
}