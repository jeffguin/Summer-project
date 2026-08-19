using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class AudiencePoseNetworkHub : NetworkBehaviour
{
    private const float PoseTimeoutSeconds = 0.75f;
    private const float MaximumPoseDistance = 50f;

    [Header("Network")]
    [Tooltip("每秒发送观众头部、右手和屏幕接触状态的次数。")]
    [SerializeField, Min(1f)] private float poseSendRate = 30f;

    [Header("Actor Visuals")]
    [Tooltip("演员端视觉物体跟随网络姿态的平滑速度。")]
    [SerializeField, Min(0f)] private float smoothingSpeed = 24f;

    [Tooltip("收到第一帧有效姿态前是否隐藏演员端头手模型。测试阶段建议关闭。")]
    [SerializeField] private bool hideBeforeFirstValidPose;

    [Tooltip("姿态超过 0.75 秒未更新后是否隐藏模型。测试阶段建议关闭。")]
    [SerializeField] private bool hideWhenPoseStale;

    private AudiencePoseSourceProvider audienceSourceProvider;

    private Transform actorHeadTarget;
    private Transform actorRightHandTarget;

    private Vector3 receivedHeadPosition;
    private Quaternion receivedHeadRotation = Quaternion.identity;
    private Vector3 receivedRightHandPosition;
    private Quaternion receivedRightHandRotation = Quaternion.identity;

    private float lastHeadPoseReceiveTime = float.NegativeInfinity;
    private float lastRightHandPoseReceiveTime = float.NegativeInfinity;
    private float lastAudienceStateReceiveTime = float.NegativeInfinity;
    private float lastAudienceScreenContactTime = float.NegativeInfinity;

    private float nextPoseSendTime;
    private float nextSourceResolveTime;
    private float nextTargetResolveTime;

    private AudienceVirtualHighFiveController audienceClapHaptic;

    private bool hasEverReceivedHeadPose;
    private bool hasEverReceivedRightHandPose;
    private bool receivedHeadPoseValid;
    private bool receivedRightHandPoseValid;
    private bool receivedAudienceHandNearScreen;
    private bool snapHeadTargetOnNextUpdate;
    private bool snapRightHandTargetOnNextUpdate;

    private bool loggedSourcesReady;
    private bool loggedMissingSources;
    private bool loggedTargetsReady;
    private bool loggedMissingTargets;

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            TryResolveActorTargets();
            ApplyInitialActorTargetVisibility();
        }
        else
        {
            TryResolveAudienceSourceProvider();
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (!hasState)
            return;

        SetTargetVisibility(actorHeadTarget, false);
        SetTargetVisibility(actorRightHandTarget, false);
    }

    private void OnValidate()
    {
        poseSendRate = Mathf.Max(1f, poseSendRate);
        smoothingSpeed = Mathf.Max(0f, smoothingSpeed);
    }

    private void Update()
    {
        if (Object == null ||
            !Object.IsValid ||
            Runner == null ||
            !Runner.IsRunning)
        {
            return;
        }

        if (Object.HasStateAuthority)
            UpdateActorTargets();
        else
            SendAudienceStateWhenReady();
    }

    public bool HasRecentAudienceScreenContact(float graceSeconds)
    {
        if (Object == null ||
            !Object.IsValid ||
            !Object.HasStateAuthority ||
            Runner == null ||
            !Runner.IsRunning)
        {
            return false;
        }

        float now = Time.realtimeSinceStartup;

        if (now - lastAudienceStateReceiveTime > PoseTimeoutSeconds)
            return false;

        if (receivedAudienceHandNearScreen)
            return true;

        return now - lastAudienceScreenContactTime <=
               Mathf.Max(0f, graceSeconds);
    }

    public bool TryNotifyAudienceClap()
    {
        if (Object == null ||
            !Object.IsValid ||
            !Object.HasStateAuthority ||
            Runner == null ||
            !Runner.IsRunning)
        {
            return false;
        }

        RPC_PlayAudienceClapHaptic();
        return true;
    }

    private void SendAudienceStateWhenReady()
    {
        if (Time.unscaledTime < nextPoseSendTime)
            return;

        nextPoseSendTime =
            Time.unscaledTime + 1f / Mathf.Max(1f, poseSendRate);

        if (audienceSourceProvider == null ||
            !audienceSourceProvider.isActiveAndEnabled)
        {
            if (Time.unscaledTime >= nextSourceResolveTime)
            {
                nextSourceResolveTime = Time.unscaledTime + 0.5f;
                TryResolveAudienceSourceProvider();
            }

            return;
        }

        bool headValid = audienceSourceProvider.TryGetHeadPose(
            out Vector3 headPosition,
            out Quaternion headRotation
        );

        bool rightHandValid =
            audienceSourceProvider.TryGetRightHandPose(
                out Vector3 rightHandPosition,
                out Quaternion rightHandRotation
            );

        bool rightHandNearScreen =
            audienceSourceProvider.IsRightHandNearScreen(rightHandValid);

        RPC_SubmitAudienceState(
            headValid,
            headPosition,
            headRotation,
            rightHandValid,
            rightHandPosition,
            rightHandRotation,
            rightHandNearScreen
        );
    }

    private void TryResolveAudienceSourceProvider()
    {
        audienceSourceProvider =
            FindFirstObjectByType<AudiencePoseSourceProvider>(
                FindObjectsInactive.Exclude
            );

        if (audienceSourceProvider != null && !loggedSourcesReady)
        {
            loggedSourcesReady = true;
            loggedMissingSources = false;
            Debug.Log(
                "AudiencePoseNetworkHub: Explicit audience sources bound. " +
                "Head=" + audienceSourceProvider.HeadSourceName +
                ", RightHand=" +
                audienceSourceProvider.RightHandSourceName + ".",
                this
            );
        }
        else if (audienceSourceProvider == null && !loggedMissingSources)
        {
            loggedMissingSources = true;
            Debug.LogWarning(
                "AudiencePoseNetworkHub: Waiting for an active " +
                "AudiencePoseSourceProvider.",
                this
            );
        }
    }

    private void UpdateActorTargets()
    {
        if ((actorHeadTarget == null || actorRightHandTarget == null) &&
            Time.unscaledTime >= nextTargetResolveTime)
        {
            nextTargetResolveTime = Time.unscaledTime + 0.5f;
            TryResolveActorTargets();
        }

        float now = Time.realtimeSinceStartup;

        bool headPoseFresh =
            hasEverReceivedHeadPose &&
            receivedHeadPoseValid &&
            now - lastHeadPoseReceiveTime <= PoseTimeoutSeconds;

        bool rightHandPoseFresh =
            hasEverReceivedRightHandPose &&
            receivedRightHandPoseValid &&
            now - lastRightHandPoseReceiveTime <= PoseTimeoutSeconds;

        UpdateActorTarget(
            actorHeadTarget,
            headPoseFresh,
            hasEverReceivedHeadPose,
            receivedHeadPosition,
            receivedHeadRotation,
            ref snapHeadTargetOnNextUpdate
        );

        UpdateActorTarget(
            actorRightHandTarget,
            rightHandPoseFresh,
            hasEverReceivedRightHandPose,
            receivedRightHandPosition,
            receivedRightHandRotation,
            ref snapRightHandTargetOnNextUpdate
        );
    }

    private void UpdateActorTarget(
        Transform target,
        bool poseIsFresh,
        bool hasEverReceivedPose,
        Vector3 position,
        Quaternion rotation,
        ref bool snapOnNextUpdate)
    {
        if (target == null)
            return;

        bool shouldHide = !poseIsFresh &&
            ((!hasEverReceivedPose && hideBeforeFirstValidPose) ||
             (hasEverReceivedPose && hideWhenPoseStale));

        SetTargetVisibility(target, !shouldHide);

        if (!poseIsFresh)
            return;

        if (snapOnNextUpdate)
        {
            target.SetPositionAndRotation(position, rotation);
            snapOnNextUpdate = false;
            return;
        }

        float blend = smoothingSpeed <= 0f
            ? 1f
            : 1f - Mathf.Exp(-smoothingSpeed * Time.deltaTime);

        ApplySmoothedPose(target, position, rotation, blend);
    }

    private void TryResolveActorTargets()
    {
        AudiencePoseVisualTarget[] targets =
            FindObjectsByType<AudiencePoseVisualTarget>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        actorHeadTarget = null;
        actorRightHandTarget = null;

        foreach (AudiencePoseVisualTarget target in targets)
        {
            if (target == null)
                continue;

            if (target.Kind == AudiencePoseVisualTarget.TargetKind.Head &&
                actorHeadTarget == null)
            {
                actorHeadTarget = target.transform;
            }
            else if (target.Kind ==
                     AudiencePoseVisualTarget.TargetKind.RightHand &&
                     actorRightHandTarget == null)
            {
                actorRightHandTarget = target.transform;
            }
        }

        bool foundBoth =
            actorHeadTarget != null &&
            actorRightHandTarget != null;

        if (foundBoth && !loggedTargetsReady)
        {
            loggedTargetsReady = true;
            loggedMissingTargets = false;
            Debug.Log(
                "AudiencePoseNetworkHub: Actor AudienceHead and " +
                "AudienceHand visual targets bound.",
                this
            );
            ApplyInitialActorTargetVisibility();
        }
        else if (!foundBoth && !loggedMissingTargets)
        {
            loggedMissingTargets = true;
            Debug.LogWarning(
                "AudiencePoseNetworkHub: Waiting for actor-side " +
                "AudiencePoseVisualTarget components for Head and " +
                "RightHand.",
                this
            );
        }
    }

    private void ApplyInitialActorTargetVisibility()
    {
        bool visible = !hideBeforeFirstValidPose;
        SetTargetVisibility(actorHeadTarget, visible);
        SetTargetVisibility(actorRightHandTarget, visible);
    }

    private static void SetTargetVisibility(
        Transform target,
        bool visible)
    {
        if (target != null && target.gameObject.activeSelf != visible)
            target.gameObject.SetActive(visible);
    }

    [Rpc(
        sources: RpcSources.All,
        targets: RpcTargets.StateAuthority,
        Channel = RpcChannel.Unreliable,
        TickAligned = false,
        HostMode = RpcHostMode.SourceIsHostPlayer
    )]
    private void RPC_SubmitAudienceState(
        bool headValid,
        Vector3 headPosition,
        Quaternion headRotation,
        bool rightHandValid,
        Vector3 rightHandPosition,
        Quaternion rightHandRotation,
        bool rightHandNearScreen,
        RpcInfo info = default)
    {
        if (Runner == null ||
            info.Source == PlayerRef.None ||
            info.Source == Runner.LocalPlayer ||
            info.Source != GetOnlyOtherPlayer())
        {
            return;
        }

        if (headValid && !IsReasonablePose(headPosition, headRotation))
            headValid = false;

        if (rightHandValid &&
            !IsReasonablePose(rightHandPosition, rightHandRotation))
        {
            rightHandValid = false;
        }

        float now = Time.realtimeSinceStartup;
        lastAudienceStateReceiveTime = now;
        receivedAudienceHandNearScreen =
            rightHandValid && rightHandNearScreen;

        if (receivedAudienceHandNearScreen)
            lastAudienceScreenContactTime = now;

        receivedHeadPoseValid = headValid;
        if (headValid)
        {
            bool wasFresh =
                hasEverReceivedHeadPose &&
                now - lastHeadPoseReceiveTime <= PoseTimeoutSeconds;

            receivedHeadPosition = headPosition;
            receivedHeadRotation = headRotation.normalized;
            lastHeadPoseReceiveTime = now;
            hasEverReceivedHeadPose = true;

            if (!wasFresh)
                snapHeadTargetOnNextUpdate = true;
        }

        receivedRightHandPoseValid = rightHandValid;
        if (rightHandValid)
        {
            bool wasFresh =
                hasEverReceivedRightHandPose &&
                now - lastRightHandPoseReceiveTime <= PoseTimeoutSeconds;

            receivedRightHandPosition = rightHandPosition;
            receivedRightHandRotation = rightHandRotation.normalized;
            lastRightHandPoseReceiveTime = now;
            hasEverReceivedRightHandPose = true;

            if (!wasFresh)
                snapRightHandTargetOnNextUpdate = true;
        }
    }

    [Rpc(
        RpcSources.StateAuthority,
        RpcTargets.All,
        Channel = RpcChannel.Reliable,
        TickAligned = false
    )]
    private void RPC_PlayAudienceClapHaptic()
    {
        if (Object == null || Object.HasStateAuthority)
            return;

        if (audienceClapHaptic == null)
        {
            audienceClapHaptic =
                FindFirstObjectByType<AudienceVirtualHighFiveController>(
                    FindObjectsInactive.Include
                );
        }

        if (audienceClapHaptic != null)
        {
            audienceClapHaptic.PlayNetworkClapHaptic();
        }
        else
        {
            Debug.LogWarning(
                "AudiencePoseNetworkHub: The audience clap haptic " +
                "controller could not be found.",
                this
            );
        }
    }

    private PlayerRef GetOnlyOtherPlayer()
    {
        if (Runner == null)
            return PlayerRef.None;

        PlayerRef result = PlayerRef.None;
        int count = 0;

        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            if (player == Runner.LocalPlayer)
                continue;

            result = player;
            count++;
        }

        return count == 1 ? result : PlayerRef.None;
    }

    private static void ApplySmoothedPose(
        Transform target,
        Vector3 position,
        Quaternion rotation,
        float blend)
    {
        target.position = Vector3.Lerp(target.position, position, blend);
        target.rotation = Quaternion.Slerp(target.rotation, rotation, blend);
    }

    private static bool IsReasonablePose(
        Vector3 position,
        Quaternion rotation)
    {
        return IsFinite(position) &&
               IsFinite(rotation) &&
               position.sqrMagnitude <=
               MaximumPoseDistance * MaximumPoseDistance &&
               Quaternion.Dot(rotation, rotation) > 0.0001f;
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) &&
               IsFinite(value.y) &&
               IsFinite(value.z);
    }

    private static bool IsFinite(Quaternion value)
    {
        return IsFinite(value.x) &&
               IsFinite(value.y) &&
               IsFinite(value.z) &&
               IsFinite(value.w);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
