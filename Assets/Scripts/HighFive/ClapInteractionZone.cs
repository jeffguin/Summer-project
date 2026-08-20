using UnityEngine;

[DefaultExecutionOrder(300)]
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class ClapInteractionZone : MonoBehaviour
{
    [Header("Clap Volume")]
    [Tooltip("演员手和网络同步后的观众右手共同使用的演员端屏幕区域。")]
    [SerializeField] private BoxCollider clapVolume;

    [Tooltip("两次拍手触发之间至少间隔多少秒。触发后还必须先离开区域才能再次触发。")]
    [SerializeField, Min(0f)] private float cooldownSeconds = 5f;

    [Tooltip("允许观众手和演员手先后进入区域的最大时间差，用于吸收动作及网络延迟。")]
    [SerializeField, Min(0f)] private float synchronizationGraceSeconds =
        0.4f;

    [Tooltip("在 claphand 前后额外允许的接触深度（米）。手掌中心和 Vive 手柄原点无法真正穿入实体屏幕，因此需要保留容差。")]
    [SerializeField, Min(0f)] private float contactDepthToleranceMeters =
        0.20f;

    [Header("Tracked Hands")]
    [Tooltip("可选。留空时自动使用本机演员 Avatar 的左手骨骼。")]
    [SerializeField] private Transform actorLeftHand;

    [Tooltip("可选。留空时自动使用本机演员 Avatar 的右手骨骼。")]
    [SerializeField] private Transform actorRightHand;

    [Header("Diagnostics")]
    [SerializeField] private bool debugLog;

    private AudiencePoseNetworkHub poseNetworkHub;
    private float nextResolveTime;
    private float nextAllowedClapTime;
    private float lastActorContactTime = float.NegativeInfinity;
    private float lastAudienceContactTime = float.NegativeInfinity;
    private bool clapConditionWasMet;
    private bool loggedReady;
    private bool previousActorHandInside;
    private bool previousAudienceHandInside;

    private void Awake()
    {
        if (clapVolume == null)
            clapVolume = GetComponent<BoxCollider>();

        ResolveRuntimeReferences();
    }

    private void OnValidate()
    {
        cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
        synchronizationGraceSeconds =
            Mathf.Max(0f, synchronizationGraceSeconds);
        contactDepthToleranceMeters =
            Mathf.Max(0f, contactDepthToleranceMeters);

        if (clapVolume == null)
            clapVolume = GetComponent<BoxCollider>();

        if (clapVolume != null)
            clapVolume.isTrigger = true;
    }

    private void Update()
    {
        if (!ReferencesAreReady() &&
            Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.5f;
            ResolveRuntimeReferences();
        }

        if (!ReferencesAreReady())
        {
            clapConditionWasMet = false;
            lastActorContactTime = float.NegativeInfinity;
            lastAudienceContactTime = float.NegativeInfinity;
            return;
        }

        bool actorHandInside =
            IsActiveHandInside(actorLeftHand) ||
            IsActiveHandInside(actorRightHand);
        bool audienceHandPoseIsFresh =
            poseNetworkHub.TryGetRecentAudienceRightHandPosition(
                out Vector3 audienceHandPosition
            );
        bool audienceHandInside =
            audienceHandPoseIsFresh &&
            IsPointInsideVolume(audienceHandPosition);

        float now = Time.unscaledTime;

        if (actorHandInside)
            lastActorContactTime = now;
        if (audienceHandInside)
            lastAudienceContactTime = now;

        bool actorContactIsRecent =
            now - lastActorContactTime <= synchronizationGraceSeconds;
        bool audienceContactIsRecent =
            now - lastAudienceContactTime <= synchronizationGraceSeconds;

        bool clapConditionMet =
            actorContactIsRecent && audienceContactIsRecent;

        poseNetworkHub.ReportClapDetectionState(
            zoneReady: true,
            actorHandInside,
            audienceHandInside,
            audienceHandPoseIsFresh
        );

        LogContactStateChanges(actorHandInside, audienceHandInside);

        if (clapConditionMet &&
            !clapConditionWasMet &&
            now >= nextAllowedClapTime &&
            poseNetworkHub.TryNotifyAudienceClap())
        {
            nextAllowedClapTime =
                now + cooldownSeconds;

            if (debugLog)
            {
                Debug.Log(
                    "[ClapInteractionZone] Actor hand and synchronized " +
                    "AudienceHand entered the same claphand volume. " +
                    "Audience haptic requested.",
                    this
                );
            }
        }

        clapConditionWasMet = clapConditionMet;
    }

    private void OnDisable()
    {
        if (poseNetworkHub != null)
        {
            poseNetworkHub.ReportClapDetectionState(
                zoneReady: false,
                actorHandInside: false,
                audienceHandInside: false,
                audienceHandPoseIsFresh: false
            );
        }
    }

    private bool ReferencesAreReady()
    {
        return clapVolume != null &&
               (actorLeftHand != null || actorRightHand != null) &&
               poseNetworkHub != null;
    }

    private void ResolveRuntimeReferences()
    {
        if (clapVolume == null)
            clapVolume = GetComponent<BoxCollider>();

        if (poseNetworkHub == null)
        {
            poseNetworkHub = FindFirstObjectByType<AudiencePoseNetworkHub>(
                FindObjectsInactive.Include
            );
        }

        if (actorLeftHand == null || actorRightHand == null)
            ResolveLocalActorHands();

        if (ReferencesAreReady() && !loggedReady)
        {
            loggedReady = true;

            if (debugLog)
            {
                Debug.Log(
                    "[ClapInteractionZone] Shared clap volume, local " +
                    "actor hand and audience pose network hub are ready.",
                    this
                );
            }
        }
    }

    private void LogContactStateChanges(
        bool actorHandInside,
        bool audienceHandInside)
    {
        if (debugLog &&
            actorHandInside != previousActorHandInside)
        {
            Debug.Log(
                "[ClapInteractionZone] Actor hand inside=" +
                actorHandInside + ".",
                this
            );
        }

        if (debugLog &&
            audienceHandInside != previousAudienceHandInside)
        {
            Debug.Log(
                "[ClapInteractionZone] AudienceHand inside=" +
                audienceHandInside + ".",
                this
            );
        }

        previousActorHandInside = actorHandInside;
        previousAudienceHandInside = audienceHandInside;
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

    private bool IsActiveHandInside(Transform hand)
    {
        return hand != null &&
               hand.gameObject.activeInHierarchy &&
               IsPointInsideVolume(hand.position);
    }

    private bool IsPointInsideVolume(Vector3 worldPosition)
    {
        Vector3 localPoint =
            clapVolume.transform.InverseTransformPoint(worldPosition) -
            clapVolume.center;
        Vector3 halfSize = clapVolume.size * 0.5f;
        float depthScale =
            Mathf.Abs(clapVolume.transform.lossyScale.z);
        float localDepthTolerance = depthScale > 0.0001f
            ? contactDepthToleranceMeters / depthScale
            : 0f;

        return Mathf.Abs(localPoint.x) <= halfSize.x &&
               Mathf.Abs(localPoint.y) <= halfSize.y &&
               Mathf.Abs(localPoint.z) <=
                   halfSize.z + localDepthTolerance;
    }
}
