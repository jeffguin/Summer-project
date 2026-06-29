using UnityEngine;

public class EyeTrackingBridge : MonoBehaviour
{
    public EyeGazeController controller;

    private OVRPlugin.EyeGazesState state;

    void Update()
    {
        bool success = OVRPlugin.GetEyeGazesState(
            OVRPlugin.Step.Render,
            0,
            ref state
        );

        if (!success)
            return;

        // Convert LEFT eye rotation
        Quaternion leftRot = ToUnityQuat(state.EyeGazes[0].Pose.Orientation);

        // Convert RIGHT eye rotation
        Quaternion rightRot = ToUnityQuat(state.EyeGazes[1].Pose.Orientation);

        Vector3 leftDir = leftRot * Vector3.forward;
        Vector3 rightDir = rightRot * Vector3.forward;

        Vector3 dir = (leftDir + rightDir) * 0.5f;

        controller.gazeX = dir.x;
        controller.gazeY = dir.y;
    }

    private Quaternion ToUnityQuat(OVRPlugin.Quatf q)
    {
        return new Quaternion(q.x, q.y, q.z, q.w);
    }
}