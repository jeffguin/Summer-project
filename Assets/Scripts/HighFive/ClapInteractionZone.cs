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

    [Tooltip("优先使用具有本地输入权限的 Actor Avatar 左右手骨；场景中绑定的 OVR palm marker 仅作为 Avatar 尚未生成时的后备。")]
    [SerializeField] private bool preferLocalActorAvatarHands = true;

    [Tooltip("运行时添加到演员手掌上的球形碰撞探针半径（米）。")]
    [SerializeField, Min(0.005f)]
    private float actorHandProbeRadius = 0.04f;

    [Header("Diagnostics")]
    [SerializeField] private bool debugLog;

    private const string LeftHandProbeObjectName =
        "ActorLeftClapHandProbe";
    private const string RightHandProbeObjectName =
        "ActorRightClapHandProbe";

    private readonly HashSet<SphereCollider> actorHandsInside =
        new HashSet<SphereCollider>();

    private AudiencePoseNetworkHub poseNetworkHub;
    private Rigidbody triggerBody;
    private Transform trackedActorLeftHand;
    private Transform trackedActorRightHand;
    private SphereCollider leftHandProbe;
    private SphereCollider rightHandProbe;
    private Rigidbody leftHandProbeBody;
    private Rigidbody rightHandProbeBody;
    private float nextResolveTime;
    private bool loggedReady;
    private bool triggerAvailable;
    private bool leftHandProbePositioned;
    private bool rightHandProbePositioned;
    private bool usingLocalAvatarHands;

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
        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.5f;
            ResolveRuntimeReferences();
        }

        UpdateActorHandProbes();
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

        SetHandProbeEnabled(leftHandProbe, false);
        SetHandProbeEnabled(rightHandProbe, false);
        leftHandProbePositioned = false;
        rightHandProbePositioned = false;

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
               ((trackedActorLeftHand != null &&
                 leftHandProbe != null &&
                 leftHandProbeBody != null) ||
                (trackedActorRightHand != null &&
                 rightHandProbe != null &&
                 rightHandProbeBody != null));
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

        if (audienceRightHandVisual == null)
            ResolveAudienceRightHandVisual();

        ResolveActorHandSources();

        leftHandProbe = EnsureActorHandProbe(
            LeftHandProbeObjectName,
            leftHandProbe,
            ref leftHandProbeBody
        );
        rightHandProbe = EnsureActorHandProbe(
            RightHandProbeObjectName,
            rightHandProbe,
            ref rightHandProbeBody
        );

        if (ReferencesAreReady() && !loggedReady)
        {
            loggedReady = true;

            if (debugLog)
            {
                Debug.Log(
                    "[ClapInteractionZone] AudienceHand-following " +
                    "trigger and independent actor hand physics " +
                    "probes are ready. LeftSource=" +
                    GetHierarchyPath(trackedActorLeftHand) +
                    ", RightSource=" +
                    GetHierarchyPath(trackedActorRightHand) + ".",
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
        string probeObjectName,
        SphereCollider currentProbe,
        ref Rigidbody probeBody)
    {
        SphereCollider probe = currentProbe;
        if (probe == null)
        {
            GameObject probeObject = new GameObject(probeObjectName);
            probeObject.layer = gameObject.layer;
            probe = probeObject.AddComponent<SphereCollider>();
            probeBody = probeObject.AddComponent<Rigidbody>();
            probe.enabled = false;
        }

        if (probeBody == null)
            probeBody = probe.GetComponent<Rigidbody>();

        if (probeBody == null)
            probeBody = probe.gameObject.AddComponent<Rigidbody>();

        probe.isTrigger = false;
        probe.center = Vector3.zero;
        probe.radius = actorHandProbeRadius;

        ConfigureKinematicBody(probeBody);
        return probe;
    }

    private static void ConfigureKinematicBody(Rigidbody body)
    {
        body.useGravity = false;
        body.isKinematic = true;
        body.detectCollisions = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode =
            CollisionDetectionMode.ContinuousSpeculative;
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

    private void ResolveActorHandSources()
    {
        Transform avatarLeftHand = null;
        Transform avatarRightHand = null;

        if (preferLocalActorAvatarHands)
        {
            TryResolveLocalActorAvatarHands(
                out avatarLeftHand,
                out avatarRightHand
            );
        }

        Transform desiredLeftHand = avatarLeftHand != null
            ? avatarLeftHand
            : actorLeftHand;
        Transform desiredRightHand = avatarRightHand != null
            ? avatarRightHand
            : actorRightHand;
        bool nowUsingLocalAvatarHands =
            avatarLeftHand != null || avatarRightHand != null;
        bool sourceChanged =
            desiredLeftHand != trackedActorLeftHand ||
            desiredRightHand != trackedActorRightHand;

        if (!sourceChanged)
            return;

        trackedActorLeftHand = desiredLeftHand;
        trackedActorRightHand = desiredRightHand;
        usingLocalAvatarHands = nowUsingLocalAvatarHands;
        leftHandProbePositioned = false;
        rightHandProbePositioned = false;
        actorHandsInside.Clear();

        SetHandProbeEnabled(leftHandProbe, false);
        SetHandProbeEnabled(rightHandProbe, false);

        if (debugLog)
        {
            Debug.Log(
                "[ClapInteractionZone] Actor hand sources changed. " +
                "UsingLocalAvatarHands=" + usingLocalAvatarHands +
                ", Left=" + GetHierarchyPath(trackedActorLeftHand) +
                ", Right=" + GetHierarchyPath(trackedActorRightHand) +
                ".",
                this
            );
        }
    }

    private bool TryResolveLocalActorAvatarHands(
        out Transform leftHand,
        out Transform rightHand)
    {
        leftHand = null;
        rightHand = null;

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

                leftHand =
                    animator.GetBoneTransform(HumanBodyBones.LeftHand);
                rightHand =
                    animator.GetBoneTransform(HumanBodyBones.RightHand);

                if (leftHand == null && rightHand == null)
                    continue;

                return true;
            }
        }

        return false;
    }

    private void UpdateActorHandProbes()
    {
        UpdateActorHandProbe(
            trackedActorLeftHand,
            leftHandProbe,
            leftHandProbeBody,
            ref leftHandProbePositioned
        );
        UpdateActorHandProbe(
            trackedActorRightHand,
            rightHandProbe,
            rightHandProbeBody,
            ref rightHandProbePositioned
        );
    }

    private void UpdateActorHandProbe(
        Transform handSource,
        SphereCollider probe,
        Rigidbody probeBody,
        ref bool probePositioned)
    {
        bool sourceAvailable =
            handSource != null &&
            handSource.gameObject.activeInHierarchy &&
            probe != null &&
            probeBody != null;

        if (!sourceAvailable)
        {
            SetHandProbeEnabled(probe, false);
            actorHandsInside.Remove(probe);
            probePositioned = false;
            return;
        }

        Vector3 targetPosition = handSource.position;
        Quaternion targetRotation = handSource.rotation;

        if (!probePositioned)
        {
            probeBody.position = targetPosition;
            probeBody.rotation = targetRotation;
            probeBody.WakeUp();
            probePositioned = true;
        }
        else
        {
            probeBody.MovePosition(targetPosition);
            probeBody.MoveRotation(targetRotation);
        }

        SetHandProbeEnabled(probe, true);
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

    private static void SetHandProbeEnabled(
        SphereCollider probe,
        bool enabled)
    {
        if (probe != null && probe.enabled != enabled)
            probe.enabled = enabled;
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

    private static string GetHierarchyPath(Transform target)
    {
        if (target == null)
            return "None";

        string path = target.name;
        Transform parent = target.parent;

        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }

    private void OnDestroy()
    {
        DestroyRuntimeProbe(leftHandProbe);
        DestroyRuntimeProbe(rightHandProbe);
    }

    private static void DestroyRuntimeProbe(SphereCollider probe)
    {
        if (probe == null)
            return;

        if (Application.isPlaying)
            Destroy(probe.gameObject);
        else
            DestroyImmediate(probe.gameObject);
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
