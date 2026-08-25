using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class PortalCameraController : MonoBehaviour
{
    [Header("Physical Setup")]
    public Transform screen;
    public Transform eye;

    [Header("Adaptive Projection")]
    public float idealMinDistance = 0.6f;
    public float idealMaxDistance = 1.5f;

    public float closeLimitDistance = 0.3f;
    public float farLimitDistance = 3.0f;

    public float maximumEffectiveCloseDistance = 0.6f;
    public float minimumEffectiveFarDistance = 1.5f;

    [Header("Cadre")]
    [Tooltip("Cadre used as the portal boundary.")]
    public Transform cadre;

    [Tooltip("Enable the cadre when the audience exceeds the far limit.")]
    public bool enableCadreAtFarLimit = true;

    public bool IsBeyondFarLimit { get; private set; }

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
        // ---------------------------------------------
        // SCREEN CORNERS
        // ---------------------------------------------

        Vector3 bl =
            screen.TransformPoint(
                new Vector3(-0.5f, -0.5f, 0f)
            );

        Vector3 br =
            screen.TransformPoint(
                new Vector3(0.5f, -0.5f, 0f)
            );

        Vector3 tl =
            screen.TransformPoint(
                new Vector3(-0.5f, 0.5f, 0f)
            );

        // ---------------------------------------------
        // SCREEN BASIS
        // ---------------------------------------------

        Vector3 vr =
            (br - bl).normalized;

        Vector3 vu =
            (tl - bl).normalized;

        Vector3 vn =
            Vector3.Cross(vr, vu).normalized;

        Vector3 eyePos = eye.position;

        if (Vector3.Dot(vn, eyePos - bl) < 0f)
            vn = -vn;

        // ---------------------------------------------
        // ACTUAL DISTANCE
        // ---------------------------------------------

        float actualDistance =
            Vector3.Dot(
                eyePos - bl,
                vn
            );

        actualDistance =
            Mathf.Max(
                actualDistance,
                0.01f
            );

        IsBeyondFarLimit =
            actualDistance > farLimitDistance;

        // ---------------------------------------------
        // EFFECTIVE DISTANCE
        // ---------------------------------------------

        float effectiveDistance =
            CalculateEffectiveDistance(
                actualDistance
            );

        // ---------------------------------------------
        // CLIP PLANES
        // ---------------------------------------------

        float near = cam.nearClipPlane;
        float far = cam.farClipPlane;

        // ---------------------------------------------
        // OFF-AXIS FRUSTUM
        //
        // This remains the wide adaptive projection.
        // The cadre does NOT modify these values.
        // ---------------------------------------------

        float left =
            Vector3.Dot(
                vr,
                bl - eyePos
            ) * near / effectiveDistance;

        float right =
            Vector3.Dot(
                vr,
                br - eyePos
            ) * near / effectiveDistance;

        float bottom =
            Vector3.Dot(
                vu,
                bl - eyePos
            ) * near / effectiveDistance;

        float top =
            Vector3.Dot(
                vu,
                tl - eyePos
            ) * near / effectiveDistance;

        cam.projectionMatrix =
            Matrix4x4.Frustum(
                left,
                right,
                bottom,
                top,
                near,
                far
            );

        // ---------------------------------------------
        // OBLIQUE CLIPPING
        //
        // The physical screen remains the portal plane.
        // ---------------------------------------------

        Vector3 cameraSpaceNormal =
            cam.worldToCameraMatrix
                .MultiplyVector(vn);

        Vector3 cameraSpacePoint =
            cam.worldToCameraMatrix
                .MultiplyPoint(bl);

        float distanceToPlane =
            -Vector3.Dot(
                cameraSpaceNormal,
                cameraSpacePoint
            );

        Vector4 clipPlane =
            new Vector4(
                cameraSpaceNormal.x,
                cameraSpaceNormal.y,
                cameraSpaceNormal.z,
                distanceToPlane
            );

        cam.projectionMatrix =
            cam.CalculateObliqueMatrix(
                clipPlane
            );

        // ---------------------------------------------
        // CAMERA TRANSFORM
        // ---------------------------------------------

        cam.transform.position =
            eyePos;

        cam.transform.rotation =
            Quaternion.LookRotation(
                -vn,
                vu
            );

        // ---------------------------------------------
        // CADRE STATE
        // ---------------------------------------------

        if (cadre != null)
        {
            bool shouldShowCadre =
                enableCadreAtFarLimit &&
                IsBeyondFarLimit;

            cadre.gameObject.SetActive(
                shouldShowCadre
            );
        }
    }

    float CalculateEffectiveDistance(
        float actualDistance
    )
    {
        // ---------------------------------------------
        // IDEAL ZONE
        // ---------------------------------------------

        if (actualDistance >= idealMinDistance &&
            actualDistance <= idealMaxDistance)
        {
            return actualDistance;
        }

        // ---------------------------------------------
        // TOO CLOSE
        // ---------------------------------------------

        if (actualDistance < idealMinDistance)
        {
            float t =
                Mathf.InverseLerp(
                    closeLimitDistance,
                    idealMinDistance,
                    actualDistance
                );

            t = Mathf.Clamp01(t);

            t =
                Mathf.SmoothStep(
                    0f,
                    1f,
                    t
                );

            return Mathf.Lerp(
                maximumEffectiveCloseDistance,
                idealMinDistance,
                t
            );
        }

        // ---------------------------------------------
        // TOO FAR
        // ---------------------------------------------

        float farT =
            Mathf.InverseLerp(
                idealMaxDistance,
                farLimitDistance,
                actualDistance
            );

        farT = Mathf.Clamp01(farT);

        farT =
            Mathf.SmoothStep(
                0f,
                1f,
                farT
            );

        return Mathf.Lerp(
            idealMaxDistance,
            minimumEffectiveFarDistance,
            farT
        );
    }
}