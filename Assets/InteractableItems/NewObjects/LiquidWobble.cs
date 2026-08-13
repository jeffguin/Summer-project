using UnityEngine;

public class LiquidWobble : MonoBehaviour
{
    [SerializeField] private float maxWobble = 0.03f;
    [SerializeField] private float wobbleSpeed = 1f;
    [SerializeField] private float recovery = 1f;

    private Renderer rend;

    private Vector3 lastPosition;
    private Vector3 lastRotation;

    private float wobbleToAddX;
    private float wobbleToAddZ;

    private float time = 0.5f;

    private void Start()
    {
        rend = GetComponent<Renderer>();

        lastPosition = transform.position;
        lastRotation = transform.eulerAngles;
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;

        if (deltaTime <= 0f)
            return;

        // Gradually settle the liquid.
        wobbleToAddX = Mathf.Lerp(
            wobbleToAddX,
            0f,
            deltaTime * recovery
        );

        wobbleToAddZ = Mathf.Lerp(
            wobbleToAddZ,
            0f,
            deltaTime * recovery
        );

        // Create the back-and-forth wobble.
        time += deltaTime;

        float pulse = 2f * Mathf.PI * wobbleSpeed;

        float wobbleX =
            wobbleToAddX * Mathf.Sin(pulse * time);

        float wobbleZ =
            wobbleToAddZ * Mathf.Sin(pulse * time);

        // Send wobble values to the Shader Graph.
        rend.material.SetFloat("_WobbleX", wobbleX);
        rend.material.SetFloat("_WobbleZ", wobbleZ);

        // Work out how much the object moved.
        Vector3 velocity =
            (lastPosition - transform.position) / deltaTime;

        Vector3 currentRotation = transform.eulerAngles;

        Vector3 angularVelocity = new Vector3(
            Mathf.DeltaAngle(currentRotation.x, lastRotation.x),
            Mathf.DeltaAngle(currentRotation.y, lastRotation.y),
            Mathf.DeltaAngle(currentRotation.z, lastRotation.z)
        );

        // Add movement to wobble.
        wobbleToAddX += Mathf.Clamp(
            (velocity.x + angularVelocity.z * 0.2f) * maxWobble,
            -maxWobble,
            maxWobble
        );

        wobbleToAddZ += Mathf.Clamp(
            (velocity.z + angularVelocity.x * 0.2f) * maxWobble,
            -maxWobble,
            maxWobble
        );

        lastPosition = transform.position;
        lastRotation = currentRotation;
    }
}