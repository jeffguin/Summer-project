using UnityEngine;

public class LiquidPour : MonoBehaviour
{
    [Header("Pour Detection")]
    [SerializeField] private float pourStartAngle = 95f;

    // Direction from the body of the bottle toward the neck/mouth
    // in the bottle's LOCAL space.
    [SerializeField] private Vector3 localBottleAxis = Vector3.up;

    [Header("Pour Position")]
    // Position of the bottle mouth relative to this object.
    [SerializeField] private Vector3 localMouthOffset = new Vector3(0f, 0.5f, 0f);

    [Header("Pour Effect")]
    [SerializeField] private ParticleSystem pourPrefab;

    [Header("Liquid")]
    [SerializeField] private float startingFill = 0.8f;
    [SerializeField] private float drainSpeed = 0.1f;

    private ParticleSystem pourParticles;
    private Renderer liquidRenderer;
    private Material liquidMaterial;

    private float currentFill;
    private bool isPouring;

    private void Start()
    {
        liquidRenderer = GetComponent<Renderer>();
        liquidMaterial = liquidRenderer.material;

        currentFill = startingFill;

        liquidMaterial.SetFloat("_Fill", currentFill);

        // Create the pouring effect at runtime.
        pourParticles = Instantiate(pourPrefab);

        // Make sure it doesn't immediately play.
        pourParticles.Stop();
    }

    private void Update()
    {
        UpdatePourPosition();
        CheckPourAngle();

        if (isPouring)
        {
            DrainLiquid();
        }
    }

    private void CheckPourAngle()
    {
        Vector3 bottleDirection =
            transform.TransformDirection(localBottleAxis.normalized);

        float angle =
            Vector3.Angle(bottleDirection, Vector3.up);

        bool shouldPour =
            angle >= pourStartAngle &&
            currentFill > 0f;

        if (shouldPour && !isPouring)
        {
            StartPouring();
        }
        else if (!shouldPour && isPouring)
        {
            StopPouring();
        }
    }

    private void StartPouring()
    {
        isPouring = true;
        pourParticles.Play();
    }

    private void StopPouring()
    {
        isPouring = false;
        pourParticles.Stop();
    }

    private void DrainLiquid()
    {
        currentFill -= drainSpeed * Time.deltaTime;

        currentFill = Mathf.Clamp01(currentFill);

        liquidMaterial.SetFloat("_Fill", currentFill);

        if (currentFill <= 0f)
        {
            StopPouring();
        }
    }

    private void UpdatePourPosition()
    {
        if (pourParticles == null)
            return;

        // Work out where the bottle mouth is without needing
        // an actual PourPoint GameObject.
        pourParticles.transform.position =
            transform.TransformPoint(localMouthOffset);

        // Make the stream fall vertically downward.
        pourParticles.transform.rotation =
            Quaternion.LookRotation(Vector3.down);
    }
}