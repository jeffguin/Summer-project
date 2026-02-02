using UnityEngine;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Camera))]
public class DisableXRForThisCamera : MonoBehaviour
{
    void Awake()
    {
        var cam = GetComponent<Camera>();
        var urp = GetComponent<UniversalAdditionalCameraData>();

        cam.stereoTargetEye = StereoTargetEyeMask.None;

        if (urp != null)
        {
            urp.allowXRRendering = false;
            urp.renderType = CameraRenderType.Base;
        }
    }
}
