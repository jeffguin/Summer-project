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

    private Camera cam;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        cam.nearClipPlane = nearClip;
        cam.farClipPlane = farClip;
    }

    private void LateUpdate()
    {
        if (bottomLeft == null || bottomRight == null || topLeft == null || audienceEye == null)
            return;

        UpdateOffAxisProjection();
    }

    private void UpdateOffAxisProjection()
    {
        Vector3 pa = bottomLeft.position;
        Vector3 pb = bottomRight.position;
        Vector3 pc = topLeft.position;
        Vector3 pe = audienceEye.position;

        // 屏幕坐标轴
        Vector3 vr = (pb - pa).normalized; // screen right
        Vector3 vu = (pc - pa).normalized; // screen up
        Vector3 vn = Vector3.Cross(vr, vu).normalized; // screen normal

        // 从眼睛到屏幕三个角点的向量
        Vector3 va = pa - pe;
        Vector3 vb = pb - pe;
        Vector3 vc = pc - pe;

        float d = -Vector3.Dot(va, vn);

        if (d <= 0.001f)
        {
            Debug.LogWarning("AudienceEye is behind or too close to the screen plane.");
            return;
        }

        float l = Vector3.Dot(vr, va) * nearClip / d;
        float r = Vector3.Dot(vr, vb) * nearClip / d;
        float b = Vector3.Dot(vu, va) * nearClip / d;
        float t = Vector3.Dot(vu, vc) * nearClip / d;

        Matrix4x4 projection = PerspectiveOffCenter(l, r, b, t, nearClip, farClip);

        cam.projectionMatrix = projection;

        // 摄像机位置等于观众眼睛位置
        transform.position = pe;

        // 摄像机朝向屏幕法线方向
        Quaternion rotation = Quaternion.LookRotation(vn, vu);
        transform.rotation = rotation;
    }

    private Matrix4x4 PerspectiveOffCenter(
        float left,
        float right,
        float bottom,
        float top,
        float near,
        float far)
    {
        float x = 2.0f * near / (right - left);
        float y = 2.0f * near / (top - bottom);
        float a = (right + left) / (right - left);
        float b = (top + bottom) / (top - bottom);
        float c = -(far + near) / (far - near);
        float d = -(2.0f * far * near) / (far - near);
        float e = -1.0f;

        Matrix4x4 m = new Matrix4x4();

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
        m[3, 2] = e;
        m[3, 3] = 0;

        return m;
    }

    private void OnDisable()
    {
        if (cam != null)
        {
            cam.ResetProjectionMatrix();
        }
    }
}