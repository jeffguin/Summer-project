using UnityEngine;
using Oculus.Interaction;

public class BottleShakeHaptics : MonoBehaviour
{
    [SerializeField] private Grabbable grabbable;

    [Header("Shake Detection")]
    [SerializeField] private float accelerationThreshold = 4f;
    [SerializeField] private float maxAcceleration = 18f;
    [SerializeField] private float angularSpeedThreshold = 120f;
    [SerializeField] private float maxAngularSpeed = 700f;

    [Header("Haptics")]
    [SerializeField, Range(0f, 1f)] private float frequency = 0.8f;
    [SerializeField, Range(0f, 1f)] private float minAmplitude = 0.15f;
    [SerializeField, Range(0f, 1f)] private float maxAmplitude = 0.75f;
    [SerializeField] private float pulseDuration = 0.035f;
    [SerializeField] private float pulseInterval = 0.055f;

    private bool isGrabbed;

    private OVRInput.Controller heldController =
        OVRInput.Controller.None;

    private Vector3 lastPosition;
    private Vector3 lastVelocity;
    private Quaternion lastRotation;

    private float nextPulseTime;

    private void Awake()
    {
        if (grabbable == null)
        {
            grabbable = GetComponent<Grabbable>();
        }

        ResetMotion();
    }

    private void OnEnable()
    {
        if (grabbable != null)
        {
            grabbable.WhenPointerEventRaised += HandlePointerEvent;
        }

        ResetMotion();
    }

    private void OnDisable()
    {
        if (grabbable != null)
        {
            grabbable.WhenPointerEventRaised -= HandlePointerEvent;
        }

        StopHaptics();
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;

        if (deltaTime <= 0f)
            return;

        Vector3 currentVelocity =
            (transform.position - lastPosition) / deltaTime;

        float acceleration =
            (currentVelocity - lastVelocity).magnitude / deltaTime;

        float angularSpeed =
            Quaternion.Angle(
                lastRotation,
                transform.rotation
            ) / deltaTime;

        if (isGrabbed)
        {
            float linearShake =
                Mathf.InverseLerp(
                    accelerationThreshold,
                    maxAcceleration,
                    acceleration
                );

            float rotationalShake =
                Mathf.InverseLerp(
                    angularSpeedThreshold,
                    maxAngularSpeed,
                    angularSpeed
                );

            float shakeAmount =
                Mathf.Max(
                    linearShake,
                    rotationalShake
                );

            if (shakeAmount > 0f &&
                Time.time >= nextPulseTime)
            {
                float amplitude =
                    Mathf.Lerp(
                        minAmplitude,
                        maxAmplitude,
                        shakeAmount
                    );

                PlayHaptic(amplitude);

                nextPulseTime =
                    Time.time + pulseInterval;
            }
        }

        lastPosition = transform.position;
        lastVelocity = currentVelocity;
        lastRotation = transform.rotation;
    }

    private void HandlePointerEvent(PointerEvent pointerEvent)
    {
        if (pointerEvent.Type == PointerEventType.Select)
        {
            isGrabbed = true;

            heldController =
                GetHoldingController();

            ResetMotion();
        }

        if (pointerEvent.Type == PointerEventType.Unselect)
        {
            isGrabbed = false;

            StopHaptics();

            heldController =
                OVRInput.Controller.None;
        }
    }

    private OVRInput.Controller GetHoldingController()
    {
        float leftGrip =
            Mathf.Max(
                OVRInput.Get(
                    OVRInput.Axis1D.PrimaryHandTrigger,
                    OVRInput.Controller.LTouch
                ),
                OVRInput.Get(
                    OVRInput.Axis1D.PrimaryIndexTrigger,
                    OVRInput.Controller.LTouch
                )
            );

        float rightGrip =
            Mathf.Max(
                OVRInput.Get(
                    OVRInput.Axis1D.PrimaryHandTrigger,
                    OVRInput.Controller.RTouch
                ),
                OVRInput.Get(
                    OVRInput.Axis1D.PrimaryIndexTrigger,
                    OVRInput.Controller.RTouch
                )
            );

        if (leftGrip > rightGrip)
        {
            return OVRInput.Controller.LTouch;
        }

        if (rightGrip > leftGrip)
        {
            return OVRInput.Controller.RTouch;
        }

        OVRInput.Controller activeController =
            OVRInput.GetActiveController();

        if (activeController == OVRInput.Controller.LTouch)
        {
            return OVRInput.Controller.LTouch;
        }

        if (activeController == OVRInput.Controller.RTouch)
        {
            return OVRInput.Controller.RTouch;
        }

        return OVRInput.Controller.Touch;
    }

    private void PlayHaptic(float amplitude)
    {
        if (heldController == OVRInput.Controller.None)
            return;

        CancelInvoke(nameof(StopHaptics));

        OVRInput.SetControllerVibration(
            frequency,
            amplitude,
            heldController
        );

        Invoke(
            nameof(StopHaptics),
            pulseDuration
        );
    }

    private void StopHaptics()
    {
        CancelInvoke(nameof(StopHaptics));

        if (heldController == OVRInput.Controller.None)
            return;

        OVRInput.SetControllerVibration(
            0f,
            0f,
            heldController
        );
    }

    private void ResetMotion()
    {
        lastPosition = transform.position;
        lastRotation = transform.rotation;
        lastVelocity = Vector3.zero;
    }
}