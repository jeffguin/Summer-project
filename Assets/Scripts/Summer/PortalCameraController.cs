using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class PortalCameraController : MonoBehaviour
{
    [Header("Physical Setup")]
    public Transform screen;   // PhysicalScreen Quad
    public Transform eye;      // SimulatedEye / Vive Tracker target

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
        // ----------------------------------------------------
        // 1. GET SCREEN CORNERS
        // ----------------------------------------------------

        Vector3 bl = screen.TransformPoint(new Vector3(-0.5f, -0.5f, 0f));
        Vector3 br = screen.TransformPoint(new Vector3( 0.5f, -0.5f, 0f));
        Vector3 tl = screen.TransformPoint(new Vector3(-0.5f,  0.5f, 0f));


        // ----------------------------------------------------
        // 2. SCREEN BASIS
        // ----------------------------------------------------

        Vector3 vr = (br - bl).normalized;   // screen right
        Vector3 vu = (tl - bl).normalized;   // screen up

        Vector3 vn = Vector3.Cross(vr, vu).normalized;


        Vector3 eyePos = eye.position;


        // Make normal point towards viewer
        if (Vector3.Dot(vn, eyePos - bl) < 0)
        {
            vn = -vn;
        }


        // ----------------------------------------------------
        // 3. MOVE CAMERA FIRST
        // ----------------------------------------------------

        cam.transform.position = eyePos;

        cam.transform.rotation =
            Quaternion.LookRotation(-vn, vu);



        // ----------------------------------------------------
        // 4. CALCULATE OFF AXIS FRUSTUM
        // ----------------------------------------------------

        float near = cam.nearClipPlane;
        float far = cam.farClipPlane;


        float distance =
            Vector3.Dot(eyePos - bl, vn);


        if (distance < 0.01f)
            distance = 0.01f;


        float left =
            Vector3.Dot(vr, bl - eyePos)
            * near / distance;

        float right =
            Vector3.Dot(vr, br - eyePos)
            * near / distance;

        float bottom =
            Vector3.Dot(vu, bl - eyePos)
            * near / distance;

        float top =
            Vector3.Dot(vu, tl - eyePos)
            * near / distance;



        Matrix4x4 projection =
            Matrix4x4.Frustum(
                left,
                right,
                bottom,
                top,
                near,
                far
            );


        cam.projectionMatrix = projection;



        // ----------------------------------------------------
        // 5. OBLIQUE CLIPPING
        // ----------------------------------------------------

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
            cam.CalculateObliqueMatrix(clipPlane);
    }
}