using UnityEngine;
using UnityEngine.XR;
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

    private InputDevice heldController;

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

            heldController = default;
        }
    }

    private InputDevice GetHoldingController()
    {
        InputDevice leftController =
            InputDevices.GetDeviceAtXRNode(
                XRNode.LeftHand
            );

        InputDevice rightController =
            InputDevices.GetDeviceAtXRNode(
                XRNode.RightHand
            );

        float leftGrip = 0f;
        float leftTrigger = 0f;

        float rightGrip = 0f;
        float rightTrigger = 0f;

        leftController.TryGetFeatureValue(
            CommonUsages.grip,
            out leftGrip
        );

        leftController.TryGetFeatureValue(
            CommonUsages.trigger,
            out leftTrigger
        );

        rightController.TryGetFeatureValue(
            CommonUsages.grip,
            out rightGrip
        );

        rightController.TryGetFeatureValue(
            CommonUsages.trigger,
            out rightTrigger
        );

        float leftAmount =
            Mathf.Max(
                leftGrip,
                leftTrigger
            );

        float rightAmount =
            Mathf.Max(
                rightGrip,
                rightTrigger
            );

        if (leftAmount > rightAmount)
        {
            return leftController;
        }

        if (rightAmount > leftAmount)
        {
            return rightController;
        }

        bool leftGripButton = false;
        bool rightGripButton = false;

        leftController.TryGetFeatureValue(
            CommonUsages.gripButton,
            out leftGripButton
        );

        rightController.TryGetFeatureValue(
            CommonUsages.gripButton,
            out rightGripButton
        );

        if (leftGripButton)
        {
            return leftController;
        }

        if (rightGripButton)
        {
            return rightController;
        }

        return rightController;
    }

    private void PlayHaptic(float amplitude)
    {
        if (!heldController.isValid)
            return;

        CancelInvoke(nameof(StopHaptics));

        heldController.SendHapticImpulse(
            0,
            amplitude,
            pulseDuration
        );

        Invoke(
            nameof(StopHaptics),
            pulseDuration
        );
    }

    private void StopHaptics()
    {
        CancelInvoke(nameof(StopHaptics));

        if (!heldController.isValid)
            return;

        heldController.StopHaptics();
    }

    private void ResetMotion()
    {
        lastPosition = transform.position;
        lastRotation = transform.rotation;
        lastVelocity = Vector3.zero;
    }
}