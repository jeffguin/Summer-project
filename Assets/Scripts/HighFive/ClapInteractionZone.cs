using UnityEngine;

[DefaultExecutionOrder(300)]
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class ClapInteractionZone : MonoBehaviour
{
    [Header("Clap Volume")]
    [Tooltip("演员手和观众右手必须同时进入的屏幕区域。")]
    [SerializeField] private BoxCollider clapVolume;

    [Tooltip("两次拍手触发之间至少间隔多少秒。触发后还必须先离开区域才能再次触发。")]
    [SerializeField, Min(0f)] private float cooldownSeconds = 5f;

    [Header("Optional Actor Hand Overrides")]
    [Tooltip("可选。留空时自动使用本机演员 Avatar 的左手骨骼。")]
    [SerializeField] private Transform actorLeftHand;

    [Tooltip("可选。留空时自动使用本机演员 Avatar 的右手骨骼。")]
    [SerializeField] private Transform actorRightHand;

    [Header("Diagnostics")]
    [SerializeField] private bool debugLog;

    private Transform audienceRightHand;
    private AudiencePoseNetworkHub poseNetworkHub;
    private float nextResolveTime;
    private float nextAllowedClapTime;
    private bool bothHandsWereInside;
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
            bothHandsWereInside = false;
            return;
        }

        bool audienceHandInside =
            audienceRightHand.gameObject.activeInHierarchy &&
            IsPointInsideVolume(audienceRightHand.position);

        bool actorHandInside =
            IsActiveHandInside(actorLeftHand) ||
            IsActiveHandInside(actorRightHand);

        bool bothHandsInside = audienceHandInside && actorHandInside;

        if (bothHandsInside &&
            !bothHandsWereInside &&
            Time.unscaledTime >= nextAllowedClapTime &&
            poseNetworkHub.TryNotifyAudienceClap())
        {
            nextAllowedClapTime =
                Time.unscaledTime + cooldownSeconds;

            if (debugLog)
            {
                Debug.Log(
                    "[ClapInteractionZone] Actor and audience hands " +
                    "entered the clap volume. Audience haptic requested.",
                    this
                );
            }
        }

        bothHandsWereInside = bothHandsInside;
    }

    private bool ReferencesAreReady()
    {
        return clapVolume != null &&
               audienceRightHand != null &&
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

        if (audienceRightHand == null)
        {
            AudiencePoseVisualTarget[] targets =
                FindObjectsByType<AudiencePoseVisualTarget>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                );

            foreach (AudiencePoseVisualTarget target in targets)
            {
                if (target != null &&
                    target.Kind ==
                    AudiencePoseVisualTarget.TargetKind.RightHand)
                {
                    audienceRightHand = target.transform;
                    break;
                }
            }
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
                    "and audience right hand are ready.",
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
