using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(300)]
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class ClapInteractionZone : MonoBehaviour
{
    [Header("Audience-following Trigger")]
    [Tooltip("跟随演员端 AudienceHand 移动的拍手触发区。尺寸直接在 BoxCollider 中修改。")]
    [SerializeField] private BoxCollider clapVolume;

    [Tooltip("演员端用于显示观众右手的 AudienceHand。留空时自动寻找 Target Kind 为 RightHand 的 AudiencePoseVisualTarget。")]
    [SerializeField] private Transform audienceRightHandVisual;

    [Tooltip("触发区是否同时跟随 AudienceHand 的旋转。")]
    [SerializeField] private bool followAudienceHandRotation = true;

    [Tooltip("触发区相对 AudienceHand 的本地位置偏移。")]
    [SerializeField] private Vector3 localPositionOffset;

    [Header("Tracked Actor Hands")]
    [Tooltip("演员本地左手掌。当前场景绑定 OVR Rig 的 l_palm_center_marker。")]
    [SerializeField] private Transform actorLeftHand;

    [Tooltip("演员本地右手掌。当前场景绑定 OVR Rig 的 r_palm_center_marker。")]
    [SerializeField] private Transform actorRightHand;

    [Tooltip("运行时添加到演员手掌上的球形碰撞探针半径（米）。")]
    [SerializeField, Min(0.005f)]
    private float actorHandProbeRadius = 0.04f;

    [Header("Diagnostics")]
    [SerializeField] private bool debugLog;

    private const string HandProbeObjectName = "ClapHandProbe";

    private readonly HashSet<SphereCollider> actorHandsInside =
        new HashSet<SphereCollider>();

    private AudiencePoseNetworkHub poseNetworkHub;
    private Rigidbody triggerBody;
    private SphereCollider leftHandProbe;
    private SphereCollider rightHandProbe;
    private float nextResolveTime;
    private bool loggedReady;
    private bool triggerAvailable;

    private void Awake()
    {
        EnsureTriggerPhysics();
        SetTriggerAvailable(false);
        ResolveRuntimeReferences();
    }

    private void OnValidate()
    {
        actorHandProbeRadius =
            Mathf.Max(0.005f, actorHandProbeRadius);

        if (clapVolume == null)
            clapVolume = GetComponent<BoxCollider>();

        if (clapVolume != null)
            clapVolume.isTrigger = true;
    }

    private void FixedUpdate()
    {
        if (!ReferencesAreReady() &&
            Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.5f;
            ResolveRuntimeReferences();
        }

        RemoveUnavailableHands();

        if (!ReferencesAreReady())
        {
            SetTriggerAvailable(false);
            ReportDetectionState(
                zoneReady: false,
                audiencePoseFresh: false
            );
            return;
        }

        bool audiencePoseFresh =
            poseNetworkHub.TryGetRecentAudienceRightHandPosition(out _);
        bool audienceVisualAvailable =
            audienceRightHandVisual.gameObject.activeInHierarchy;
        bool shouldEnableTrigger =
            audiencePoseFresh && audienceVisualAvailable;

        if (shouldEnableTrigger)
            FollowAudienceHand();

        SetTriggerAvailable(shouldEnableTrigger);
        ReportDetectionState(
            zoneReady: shouldEnableTrigger,
            audiencePoseFresh
        );
    }

    private void OnTriggerEnter(Collider other)
    {
        SphereCollider probe = other as SphereCollider;

        if (probe == null ||
            (probe != leftHandProbe && probe != rightHandProbe))
        {
            return;
        }

        bool wasEmpty = actorHandsInside.Count == 0;
        actorHandsInside.Add(probe);

        if (debugLog)
        {
            Debug.Log(
                "[ClapInteractionZone] Actor hand entered " +
                "AudienceHand trigger. Hand=" +
                GetProbeHandName(probe) + ".",
                this
            );
        }

        if (!wasEmpty ||
            actorHandsInside.Count != 1 ||
            !CanTriggerClap())
        {
            return;
        }

        if (!poseNetworkHub.TryNotifyAudienceClap())
            return;

        if (debugLog)
        {
            Debug.Log(
                "[ClapInteractionZone] OnTriggerEnter requested " +
                "AudienceHand clap haptic.",
                this
            );
        }
    }

    private void OnTriggerExit(Collider other)
    {
        SphereCollider probe = other as SphereCollider;

        if (probe == null || !actorHandsInside.Remove(probe))
            return;

        if (debugLog)
        {
            Debug.Log(
                "[ClapInteractionZone] Actor hand exited " +
                "AudienceHand trigger. RemainingHands=" +
                actorHandsInside.Count + ".",
                this
            );
        }
    }

    private void OnDisable()
    {
        actorHandsInside.Clear();
        triggerAvailable = false;

        if (clapVolume != null)
            clapVolume.enabled = false;

        ReportDetectionState(
            zoneReady: false,
            audiencePoseFresh: false
        );
    }

    private bool CanTriggerClap()
    {
        return triggerAvailable &&
               poseNetworkHub != null &&
               poseNetworkHub.TryGetRecentAudienceRightHandPosition(
                   out _
               );
    }

    private bool ReferencesAreReady()
    {
        return clapVolume != null &&
               triggerBody != null &&
               poseNetworkHub != null &&
               audienceRightHandVisual != null &&
               (leftHandProbe != null || rightHandProbe != null);
    }

    private void ResolveRuntimeReferences()
    {
        EnsureTriggerPhysics();

        if (poseNetworkHub == null)
        {
            poseNetworkHub = FindFirstObjectByType<AudiencePoseNetworkHub>(
                FindObjectsInactive.Include
            );
        }

        if (actorLeftHand == null || actorRightHand == null)
            ResolveLocalActorHands();

        if (audienceRightHandVisual == null)
            ResolveAudienceRightHandVisual();

        leftHandProbe = EnsureActorHandProbe(
            actorLeftHand,
            leftHandProbe
        );
        rightHandProbe = EnsureActorHandProbe(
            actorRightHand,
            rightHandProbe
        );

        if (ReferencesAreReady() && !loggedReady)
        {
            loggedReady = true;

            if (debugLog)
            {
                Debug.Log(
                    "[ClapInteractionZone] AudienceHand-following " +
                    "trigger and actor OVR hand probes are ready.",
                    this
                );
            }
        }
    }

    private void EnsureTriggerPhysics()
    {
        if (clapVolume == null)
            clapVolume = GetComponent<BoxCollider>();

        if (clapVolume != null)
            clapVolume.isTrigger = true;

        if (triggerBody == null)
            triggerBody = GetComponent<Rigidbody>();

        if (triggerBody == null)
            triggerBody = gameObject.AddComponent<Rigidbody>();

        triggerBody.useGravity = false;
        triggerBody.isKinematic = true;
        triggerBody.detectCollisions = true;
        triggerBody.interpolation = RigidbodyInterpolation.Interpolate;
        triggerBody.collisionDetectionMode =
            CollisionDetectionMode.ContinuousSpeculative;
    }

    private SphereCollider EnsureActorHandProbe(
        Transform hand,
        SphereCollider currentProbe)
    {
        if (hand == null)
            return null;

        SphereCollider probe = currentProbe;
        if (probe == null || probe.transform.parent != hand)
        {
            Transform probeTransform = hand.Find(HandProbeObjectName);

            if (probeTransform == null)
            {
                GameObject probeObject = new GameObject(
                    HandProbeObjectName
                );
                probeTransform = probeObject.transform;
                probeTransform.SetParent(hand, false);
                probeObject.layer = hand.gameObject.layer;
            }

            probe = probeTransform.GetComponent<SphereCollider>();

            if (probe == null)
            {
                probe = probeTransform.gameObject.AddComponent<
                    SphereCollider
                >();
            }
        }

        probe.isTrigger = false;
        probe.center = Vector3.zero;
        probe.radius = actorHandProbeRadius;
        probe.enabled = true;
        return probe;
    }

    private void ResolveAudienceRightHandVisual()
    {
        AudiencePoseVisualTarget[] targets =
            FindObjectsByType<AudiencePoseVisualTarget>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        foreach (AudiencePoseVisualTarget target in targets)
        {
            if (target == null ||
                target.Kind !=
                    AudiencePoseVisualTarget.TargetKind.RightHand)
            {
                continue;
            }

            audienceRightHandVisual = target.transform;
            return;
        }
    }

    private void ResolveLocalActorHands()
    {
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
                !handler.CharacterBehaviour.HasInputAuthority)
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

                if (left == null && right == null)
                    continue;

                if (actorLeftHand == null)
                    actorLeftHand = left;
                if (actorRightHand == null)
                    actorRightHand = right;

                return;
            }
        }
    }

    private void FollowAudienceHand()
    {
        Vector3 targetPosition =
            audienceRightHandVisual.TransformPoint(localPositionOffset);
        Quaternion targetRotation = followAudienceHandRotation
            ? audienceRightHandVisual.rotation
            : Quaternion.identity;

        if (!triggerAvailable)
        {
            triggerBody.position = targetPosition;
            triggerBody.rotation = targetRotation;
            return;
        }

        triggerBody.MovePosition(targetPosition);
        triggerBody.MoveRotation(targetRotation);
    }

    private void SetTriggerAvailable(bool available)
    {
        triggerAvailable = available;

        if (clapVolume != null && clapVolume.enabled != available)
            clapVolume.enabled = available;

        if (!available)
            actorHandsInside.Clear();
    }

    private void RemoveUnavailableHands()
    {
        actorHandsInside.RemoveWhere(
            probe =>
                probe == null ||
                !probe.enabled ||
                !probe.gameObject.activeInHierarchy
        );
    }

    private static string GetProbeHandName(SphereCollider probe)
    {
        if (probe == null)
            return "MissingHand";

        Transform parent = probe.transform.parent;
        return parent != null ? parent.name : probe.name;
    }

    private void ReportDetectionState(
        bool zoneReady,
        bool audiencePoseFresh)
    {
        if (poseNetworkHub == null)
            return;

        poseNetworkHub.ReportClapDetectionState(
            zoneReady,
            actorHandInside: actorHandsInside.Count > 0,
            audienceHandInside: zoneReady,
            audienceHandPoseIsFresh: audiencePoseFresh
        );
    }
}
