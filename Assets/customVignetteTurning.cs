using UnityEngine;
using Oculus.Interaction;   // << REQUIRED for TunnelingEffect

public class customVignetteTurning : MonoBehaviour
{
    public TunnelingEffect tunneling;
    public float maxTurnSpeed = 180f;

    void Update()
    {
        // Read right thumbstick X axis (smooth turn)
        float turn = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick).x;

        float turnSpeed = Mathf.Abs(turn) * maxTurnSpeed;

        float intensity = Mathf.InverseLerp(0f, maxTurnSpeed, turnSpeed);

        tunneling.AlphaStrength = intensity;
    }
}
