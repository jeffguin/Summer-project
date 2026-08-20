using UnityEngine;

[DefaultExecutionOrder(300)]
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class ClapInteractionZone : MonoBehaviour
{
    [Header("Clap Volume")]
    [Tooltip("演员手需要进入的演员端屏幕区域。观众接触由观众端本地检测。")]
    [SerializeField] private BoxCollider clapVolume;

    [Tooltip("两次拍手触发之间至少间隔多少秒。触发后还必须先离开区域才能再次触发。")]
    [SerializeField, Min(0f)] private float cooldownSeconds = 5f;

    [Tooltip("允许观众和演员先后接近屏幕的最大时间差，用于吸收网络延迟。")]
    [SerializeField, Min(0f)] private float synchronizationGraceSeconds =
        0.4f;

    [Header("Optional Actor Hand Overrides")]
    [Tooltip("可选。留空时自动使用本机演员 Avatar 的左手骨骼。")]
    [SerializeField] private Transform actorLeftHand;

    [Tooltip("可选。留空时自动使用本机演员 Avatar 的右手骨骼。")]
    [SerializeField] private Transform actorRightHand;

    [Header("Diagnostics")]
    [SerializeField] private bool debugLog;

    private AudiencePoseNetworkHub poseNetworkHub;
    private float nextResolveTime;
    private float nextAllowedClapTime;
    private bool clapConditionWasMet;
    private bool loggedReady;

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
            return;
        }

        bool actorHandInside =
            IsActiveHandInside(actorLeftHand) ||
            IsActiveHandInside(actorRightHand);

        bool audienceHandNearScreen =
            poseNetworkHub.HasRecentAudienceScreenContact(
                synchronizationGraceSeconds
            );

        bool clapConditionMet =
            audienceHandNearScreen && actorHandInside;

        if (clapConditionMet &&
            !clapConditionWasMet &&
            Time.unscaledTime >= nextAllowedClapTime &&
            poseNetworkHub.TryNotifyAudienceClap())
        {
            nextAllowedClapTime =
                Time.unscaledTime + cooldownSeconds;

            if (debugLog)
            {
                Debug.Log(
                    "[ClapInteractionZone] Actor hand entered the actor " +
                    "screen zone while the audience reported local " +
                    "screen contact. Audience haptic requested.",
                    this
                );
            }
        }

        clapConditionWasMet = clapConditionMet;
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
                    "[ClapInteractionZone] Clap volume, local actor hand " +
                    "and audience screen-contact network state are ready.",
                    this
                );
            }
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

        return Mathf.Abs(localPoint.x) <= halfSize.x &&
               Mathf.Abs(localPoint.y) <= halfSize.y &&
               Mathf.Abs(localPoint.z) <= halfSize.z;
    }
}
