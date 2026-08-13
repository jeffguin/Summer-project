#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

using UnityEngine;
using Valve.VR;

[DefaultExecutionOrder(250)]
[DisallowMultipleComponent]
public sealed class AudienceVirtualHighFiveController : MonoBehaviour
{
    [Header("Audience Ray")]
    [SerializeField] private LineRenderer rayVisual;

    [Header("SteamVR Haptics")]
    [SerializeField] private SteamVR_Action_Vibration hapticAction;
    [SerializeField] private SteamVR_Input_Sources hapticSource =
        SteamVR_Input_Sources.RightHand;

    [Header("Contact")]
    [Tooltip("Distance from an actor hand to the visible ray that starts a high five.")]
    [SerializeField, Min(0.01f)] private float enterRadius = 0.10f;

    [Tooltip("The hand must leave this larger radius before it can trigger again.")]
    [SerializeField, Min(0.02f)] private float exitRadius = 0.14f;

    [Tooltip("Minimum time between haptic pulses from either actor hand.")]
    [SerializeField, Min(0f)] private float hapticCooldown = 0.25f;

    [Tooltip("Reject implausibly large one-frame hand sweeps, such as avatar spawn or teleport.")]
    [SerializeField, Min(0.1f)] private float maximumHandSweepDistance = 0.50f;

    [Header("Haptic Pulse")]
    [SerializeField, Min(0.01f)] private float hapticDuration = 0.065f;
    [SerializeField, Min(1f)] private float hapticFrequency = 150f;
    [SerializeField, Range(0f, 1f)] private float hapticAmplitude = 0.80f;

    [Header("Network Ray")]
    [SerializeField, Min(1f)] private float raySendRate = 20f;

    [Header("Diagnostics")]
    [SerializeField] private bool debugLog;

    private Transform _leftActorHand;
    private Transform _rightActorHand;
    private NetworkWebcamControlHub _networkHub;

    private bool _leftInside;
    private bool _rightInside;
    private bool _hasPreviousLeftPosition;
    private bool _hasPreviousRightPosition;
    private Vector3 _previousLeftPosition;
    private Vector3 _previousRightPosition;

    private float _nextActorResolveTime;
    private float _nextHubResolveTime;
    private float _nextRaySendTime;
    private float _nextHapticTime;
    private bool _loggedMissingRay;
    private bool _loggedMissingHapticAction;
    private bool _loggedHapticFailure;

    private void Awake()
    {
        ResolveLocalReferences();
    }

    private void OnValidate()
    {
        enterRadius = Mathf.Max(0.01f, enterRadius);
        exitRadius = Mathf.Max(enterRadius + 0.01f, exitRadius);
        maximumHandSweepDistance = Mathf.Max(0.1f, maximumHandSweepDistance);
        raySendRate = Mathf.Max(1f, raySendRate);
    }

    private void Update()
    {
        if (!EnsureRayVisual())
            return;

        SendRayToActorWhenReady();

        if (!TryResolveRemoteActorHands())
            return;

        CheckHand(
            _leftActorHand,
            ref _leftInside,
            ref _hasPreviousLeftPosition,
            ref _previousLeftPosition,
            "Left"
        );

        CheckHand(
            _rightActorHand,
            ref _rightInside,
            ref _hasPreviousRightPosition,
            ref _previousRightPosition,
            "Right"
        );
    }

    private void ResolveLocalReferences()
    {
        if (rayVisual == null)
        {
            LineRenderer[] renderers =
                GetComponentsInChildren<LineRenderer>(true);

            foreach (LineRenderer candidate in renderers)
            {
                if (candidate != null && candidate.name == "RayVisual")
                {
                    rayVisual = candidate;
                    break;
                }
            }
        }

        if (hapticAction == null)
        {
            hapticAction = SteamVR_Input.GetVibrationAction(
                "NewSet",
                "highFiveHaptic"
            );
        }
    }

    private bool EnsureRayVisual()
    {
        if (rayVisual != null && rayVisual.positionCount >= 2)
            return true;

        ResolveLocalReferences();

        if (rayVisual != null && rayVisual.positionCount >= 2)
            return true;

        if (!_loggedMissingRay)
        {
            Debug.LogError(
                "[VirtualHighFive] The audience RayVisual LineRenderer is missing or has fewer than two points.",
                this
            );
            _loggedMissingRay = true;
        }

        return false;
    }

    private void SendRayToActorWhenReady()
    {
        if (Time.unscaledTime < _nextRaySendTime)
            return;

        _nextRaySendTime =
            Time.unscaledTime + 1f / Mathf.Max(1f, raySendRate);

        if (_networkHub == null && Time.unscaledTime >= _nextHubResolveTime)
        {
            _nextHubResolveTime = Time.unscaledTime + 0.5f;
            _networkHub = FindFirstObjectByType<NetworkWebcamControlHub>();
        }

        if (_networkHub == null)
            return;

        Vector3 start = GetWorldRayPosition(0);
        Vector3 end = GetWorldRayPosition(rayVisual.positionCount - 1);

        _networkHub.SubmitAudienceHighFiveRay(start, end);
    }

    private bool TryResolveRemoteActorHands()
    {
        if (_leftActorHand != null && _rightActorHand != null)
            return true;

        if (Time.unscaledTime < _nextActorResolveTime)
            return false;

        _nextActorResolveTime = Time.unscaledTime + 0.5f;

        ActorMovementNetworkHandler[] handlers =
            FindObjectsByType<ActorMovementNetworkHandler>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            );

        foreach (ActorMovementNetworkHandler handler in handlers)
        {
            if (handler == null ||
                !handler.IsSetupComplete ||
                handler.Character == null ||
                handler.CharacterBehaviour == null ||
                handler.CharacterBehaviour.HasInputAuthority)
            {
                continue;
            }

            Animator[] animators =
                handler.Character.GetComponentsInChildren<Animator>(true);

            foreach (Animator animator in animators)
            {
                if (animator == null || !animator.isHuman)
                    continue;

                Transform left =
                    animator.GetBoneTransform(HumanBodyBones.LeftHand);
                Transform right =
                    animator.GetBoneTransform(HumanBodyBones.RightHand);

                if (left == null || right == null)
                    continue;

                BindActorHands(left, right);
                return true;
            }
        }

        return false;
    }

    private void BindActorHands(Transform left, Transform right)
    {
        _leftActorHand = left;
        _rightActorHand = right;
        _leftInside = false;
        _rightInside = false;
        _hasPreviousLeftPosition = false;
        _hasPreviousRightPosition = false;

        if (debugLog)
        {
            Debug.Log(
                "[VirtualHighFive] Bound the remote actor's humanoid hand bones.",
                this
            );
        }
    }

    private void CheckHand(
        Transform hand,
        ref bool wasInside,
        ref bool hasPreviousPosition,
        ref Vector3 previousPosition,
        string handName)
    {
        if (hand == null)
        {
            wasInside = false;
            hasPreviousPosition = false;
            return;
        }

        Vector3 currentPosition = hand.position;
        float currentDistance = DistancePointToVisibleRay(currentPosition);
        float entryDistance = currentDistance;

        if (hasPreviousPosition)
        {
            float sweepLength =
                Vector3.Distance(previousPosition, currentPosition);

            if (sweepLength <= maximumHandSweepDistance)
            {
                entryDistance = Mathf.Min(
                    entryDistance,
                    DistanceSweepToVisibleRay(
                        previousPosition,
                        currentPosition
                    )
                );
            }
        }

        bool isInside = wasInside
            ? currentDistance <= exitRadius
            : entryDistance <= enterRadius;

        if (isInside && !wasInside)
            PlayAudienceHaptic(handName, entryDistance);

        wasInside = isInside;
        previousPosition = currentPosition;
        hasPreviousPosition = true;
    }

    private void PlayAudienceHaptic(string handName, float distance)
    {
        if (Time.unscaledTime < _nextHapticTime)
            return;

        _nextHapticTime = Time.unscaledTime + hapticCooldown;

        if (hapticAction == null)
            ResolveLocalReferences();

        if (hapticAction == null)
        {
            if (!_loggedMissingHapticAction)
            {
                Debug.LogWarning(
                    "[VirtualHighFive] SteamVR action NewSet/highFiveHaptic was not found. Generate SteamVR actions before testing haptics.",
                    this
                );
                _loggedMissingHapticAction = true;
            }
            return;
        }

        try
        {
            hapticAction.Execute(
                0f,
                hapticDuration,
                hapticFrequency,
                hapticAmplitude,
                hapticSource
            );

            if (debugLog)
            {
                Debug.Log(
                    $"[VirtualHighFive] {handName} actor hand hit the audience ray at {distance:F3} m.",
                    this
                );
            }
        }
        catch (System.Exception exception)
        {
            if (_loggedHapticFailure)
                return;

            Debug.LogWarning(
                "[VirtualHighFive] Contact was detected, but SteamVR could not play haptics. Bind /actions/NewSet/out/highFiveHaptic to the VIVE controller haptic output. " +
                exception.Message,
                this
            );
            _loggedHapticFailure = true;
        }
    }

    private float DistancePointToVisibleRay(Vector3 point)
    {
        float minimumSqrDistance = float.PositiveInfinity;
        Vector3 segmentStart = GetWorldRayPosition(0);

        for (int i = 1; i < rayVisual.positionCount; i++)
        {
            Vector3 segmentEnd = GetWorldRayPosition(i);
            Vector3 closest = ClosestPointOnSegment(
                point,
                segmentStart,
                segmentEnd
            );

            minimumSqrDistance = Mathf.Min(
                minimumSqrDistance,
                (point - closest).sqrMagnitude
            );
            segmentStart = segmentEnd;
        }

        return Mathf.Sqrt(minimumSqrDistance);
    }

    private float DistanceSweepToVisibleRay(
        Vector3 handStart,
        Vector3 handEnd)
    {
        float minimumSqrDistance = float.PositiveInfinity;
        Vector3 rayStart = GetWorldRayPosition(0);

        for (int i = 1; i < rayVisual.positionCount; i++)
        {
            Vector3 rayEnd = GetWorldRayPosition(i);
            minimumSqrDistance = Mathf.Min(
                minimumSqrDistance,
                SegmentSegmentSqrDistance(
                    handStart,
                    handEnd,
                    rayStart,
                    rayEnd
                )
            );
            rayStart = rayEnd;
        }

        return Mathf.Sqrt(minimumSqrDistance);
    }

    private Vector3 GetWorldRayPosition(int index)
    {
        Vector3 position = rayVisual.GetPosition(index);
        return rayVisual.useWorldSpace
            ? position
            : rayVisual.transform.TransformPoint(position);
    }

    private static Vector3 ClosestPointOnSegment(
        Vector3 point,
        Vector3 start,
        Vector3 end)
    {
        Vector3 segment = end - start;
        float lengthSqr = segment.sqrMagnitude;

        if (lengthSqr < 0.000001f)
            return start;

        float t = Vector3.Dot(point - start, segment) / lengthSqr;
        return start + Mathf.Clamp01(t) * segment;
    }

    private static float SegmentSegmentSqrDistance(
        Vector3 firstStart,
        Vector3 firstEnd,
        Vector3 secondStart,
        Vector3 secondEnd)
    {
        Vector3 firstDirection = firstEnd - firstStart;
        Vector3 secondDirection = secondEnd - secondStart;
        Vector3 offset = firstStart - secondStart;

        float firstLengthSqr = Vector3.Dot(
            firstDirection,
            firstDirection
        );
        float secondLengthSqr = Vector3.Dot(
            secondDirection,
            secondDirection
        );
        float secondProjection = Vector3.Dot(
            secondDirection,
            offset
        );

        float firstT;
        float secondT;

        if (firstLengthSqr <= 0.000001f &&
            secondLengthSqr <= 0.000001f)
        {
            return offset.sqrMagnitude;
        }

        if (firstLengthSqr <= 0.000001f)
        {
            firstT = 0f;
            secondT = Mathf.Clamp01(
                secondProjection / secondLengthSqr
            );
        }
        else
        {
            float firstProjection = Vector3.Dot(
                firstDirection,
                offset
            );

            if (secondLengthSqr <= 0.000001f)
            {
                secondT = 0f;
                firstT = Mathf.Clamp01(
                    -firstProjection / firstLengthSqr
                );
            }
            else
            {
                float directionsDot = Vector3.Dot(
                    firstDirection,
                    secondDirection
                );
                float denominator =
                    firstLengthSqr * secondLengthSqr -
                    directionsDot * directionsDot;

                firstT = Mathf.Abs(denominator) > 0.000001f
                    ? Mathf.Clamp01(
                        (directionsDot * secondProjection -
                         firstProjection * secondLengthSqr) /
                        denominator
                    )
                    : 0f;

                secondT =
                    (directionsDot * firstT + secondProjection) /
                    secondLengthSqr;

                if (secondT < 0f)
                {
                    secondT = 0f;
                    firstT = Mathf.Clamp01(
                        -firstProjection / firstLengthSqr
                    );
                }
                else if (secondT > 1f)
                {
                    secondT = 1f;
                    firstT = Mathf.Clamp01(
                        (directionsDot - firstProjection) /
                        firstLengthSqr
                    );
                }
            }
        }

        Vector3 closestOffset =
            offset + firstDirection * firstT - secondDirection * secondT;

        return closestOffset.sqrMagnitude;
    }
}

#else

using UnityEngine;

// The component is intentionally inert in the Android actor build. Only the
// Windows audience machine is allowed to produce controller haptics.
public sealed class AudienceVirtualHighFiveController : MonoBehaviour
{
    private void Awake()
    {
        enabled = false;
    }
}

#endif
