using UnityEngine;
using Valve.VR;

public class SteamVRPoseDebug : MonoBehaviour
{
    [SerializeField] private SteamVR_Action_Pose poseAction;
    [SerializeField] private SteamVR_Input_Sources inputSource = SteamVR_Input_Sources.RightHand;

    private void Update()
    {
        if (poseAction == null)
        {
            Debug.LogWarning("SteamVRPoseDebug: Pose Action is not assigned.");
            return;
        }

        bool active = poseAction.GetActive(inputSource);
        bool valid = poseAction.GetPoseIsValid(inputSource);
        bool connected = poseAction.GetDeviceIsConnected(inputSource);

        Vector3 pos = poseAction.GetLocalPosition(inputSource);
        Quaternion rot = poseAction.GetLocalRotation(inputSource);

        Debug.Log(
            "SteamVRPoseDebug: " +
            "active=" + active +
            ", valid=" + valid +
            ", connected=" + connected +
            ", pos=" + pos +
            ", rot=" + rot.eulerAngles
        );
    }
}