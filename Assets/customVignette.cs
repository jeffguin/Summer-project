using UnityEngine;
using Oculus.Interaction;   // << REQUIRED for TunnelingEffect

public class customVignette : MonoBehaviour
{
    public CharacterController controller;
    public TunnelingEffect tunneling;
    public float maxSpeed = 5f;

    void Update()
    {
        float speed = controller.velocity.magnitude;

        float intensity = Mathf.InverseLerp(0f, maxSpeed, speed);

        // AlphaStrength controls vignette darkness
        tunneling.AlphaStrength = intensity;

        // Optional: narrow the tunnel more when speed increases
        // tunneling.UserFOV = Mathf.Lerp(90f, 40f, intensity);
    }
}

