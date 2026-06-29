using UnityEngine;

[RequireComponent(typeof(Camera))]
public class FishTankOffAxisCamera : MonoBehaviour
{
    [Header("Screen Corners")]
    public Transform bottomLeft;
    public Transform bottomRight;
    public Transform topLeft;

    [Header("Audience Eye")]
    public Transform audienceEye;

    [Header("Camera Settings")]
    public float nearClip = 0.01f;
    public float farClip = 1000f;

    [Header("Debug")]
    public bool debugLog = true;

    private Camera cam;
    private float timer;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        cam.nearClipPlane = nearClip;
        cam.farClipPlane = farClip;
    }

    private void LateUpdate()
    {
        if (bottomLeft == null || bottomRight == null || topLeft == null || audienceEye == null)
        {
            Debug.LogWarning("FishTankOffAxisCamera: Missing references.");
            return;
        }

        UpdateOffAxisProjection();

        if (debugLog)
        {
            timer += Time.deltaTime;

            if (timer >= 1f)
            {
                timer = 0f;

                Debug.Log(
                    "=== FishTank Debug ===" +
                    "\nAudienceEye: " + audienceEye.position.ToString("F3") +
                    "\nFishTankCamera: " + transform.position.ToString("F3") +
                    "\nBottomLeft: " + bottomLeft.position.ToString("F3") +
                    "\nBottomRight: " + bottomRight.position.ToString("F3") +
                    "\nTopLeft: " + topLeft.position.ToString("F3")
                );
            }
        }
    }

    private void UpdateOffAxisProjection()
    {
        Vector3 pa = bottomLeft.position;
        Vector3 pb = bottomRight.position;
        Vector3 pc = topLeft.position;
        Vector3 pe = audienceEye.position;

        // 先让 Camera 无条件跟随 AudienceEye
        transform.position = pe;

        Vector3 vr = (pb - pa).normalized; // screen right
        Vector3 vu = (pc - pa).normalized; // screen up

        // 尝试让屏幕法线朝向虚拟世界
        Vector3 vn = Vector3.Cross(vu, vr).normalized;

        Vector3 va = pa - pe;
        Vector3 vb = pb - pe;
        Vector3 vc = pc - pe;

        float d = Vector3.Dot(va, vn);

        // 如果方向反了，自动反转法线
        if (d <= 0.001f)
        {
            vn = -vn;
            d = Vector3.Dot(va, vn);
        }

        // 如果仍然不对，至少让 Camera 看向屏幕中心
        if (d <= 0.001f)
        {
            Vector3 screenCenter = (pa + pb + pc + (pb + pc - pa)) / 4f;
            transform.LookAt(screenCenter, vu);

            Debug.LogWarning(
                "FishTankOffAxisCamera: Invalid eye/screen relation. " +
                "Camera is looking at screen center instead. d = " + d.ToString("F4")
            );

            return;
        }

        float l = Vector3.Dot(vr, va) * nearClip / d;
        float r = Vector3.Dot(vr, vb) * nearClip / d;
        float b = Vector3.Dot(vu, va) * nearClip / d;
        float t = Vector3.Dot(vu, vc) * nearClip / d;

        cam.projectionMatrix = PerspectiveOffCenter(l, r, b, t, nearClip, farClip);
        transform.rotation = Quaternion.LookRotation(vn, vu);
    }

    private Matrix4x4 PerspectiveOffCenter(
        float left,
        float right,
        float bottom,
        float top,
        float near,
        float far)
    {
        Matrix4x4 m = new Matrix4x4();

        float x = 2.0f * near / (right - left);
        float y = 2.0f * near / (top - bottom);
        float a = (right + left) / (right - left);
        float b = (top + bottom) / (top - bottom);
        float c = -(far + near) / (far - near);
        float d = -(2.0f * far * near) / (far - near);

        m[0, 0] = x;
        m[0, 1] = 0;
        m[0, 2] = a;
        m[0, 3] = 0;

        m[1, 0] = 0;
        m[1, 1] = y;
        m[1, 2] = b;
        m[1, 3] = 0;

        m[2, 0] = 0;
        m[2, 1] = 0;
        m[2, 2] = c;
        m[2, 3] = d;

        m[3, 0] = 0;
        m[3, 1] = 0;
        m[3, 2] = -1;
        m[3, 3] = 0;

        return m;
    }

    private void OnDisable()
    {
        if (cam != null)
            cam.ResetProjectionMatrix();
    }
}