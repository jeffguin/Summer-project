using Fusion;
using UnityEngine;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using Valve.VR;
#endif

[DisallowMultipleComponent]
public sealed class AudiencePoseNetworkHub : NetworkBehaviour
{
    private const float PoseTimeoutSeconds = 0.75f;
    private const float MaximumPoseDistance = 50f;

    [Header("Audience Sources")]
    [Tooltip("观众端右手 Vive 手柄物体的名称。")]
    [SerializeField] private string rightControllerObjectName =
        "ViveRightController";

    [Header("Network")]
    [Tooltip("每秒发送观众头部和右手姿态的次数。")]
    [SerializeField, Min(1f)] private float poseSendRate = 30f;

    [Header("Actor Visuals")]
    [Tooltip("演员端视觉物体跟随网络姿态的平滑速度。")]
    [SerializeField, Min(0f)] private float smoothingSpeed = 24f;

    private DirectOpenVRTrackerReader audienceHeadReader;
    private Transform audienceHeadSource;
    private Transform audienceRightHandSource;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private SteamVR_Behaviour_Pose audienceRightHandPose;
#endif

    private Transform actorHeadTarget;
    private Transform actorRightHandTarget;
    private Vector3 receivedHeadPosition;
    private Quaternion receivedHeadRotation = Quaternion.identity;
    private Vector3 receivedRightHandPosition;
    private Quaternion receivedRightHandRotation = Quaternion.identity;
    private float lastPoseReceiveTime = float.NegativeInfinity;
    private float nextPoseSendTime;
    private float nextSourceResolveTime;
    private float nextTargetResolveTime;
    private AudienceVirtualHighFiveController audienceClapHaptic;
    private bool hasReceivedPose;
    private bool snapActorTargetsOnNextUpdate;
    private bool actorTargetsVisible;
    private bool loggedSourcesReady;
    private bool loggedMissingSources;
    private bool loggedTargetsReady;
    private bool loggedMissingTargets;

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            TryResolveActorTargets();
            SetActorTargetVisibility(false);
        }
        else
        {
            TryResolveAudienceSources();
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (hasState)
            SetActorTargetVisibility(false);
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
        {
            UpdateActorTargets();
        }
        else
        {
            SendAudiencePoseWhenReady();
        }
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

    private void SendAudiencePoseWhenReady()
    {
        if (Time.unscaledTime < nextPoseSendTime)
            return;

        nextPoseSendTime =
            Time.unscaledTime + 1f / Mathf.Max(1f, poseSendRate);

        if (!AreAudienceSourcesReady())
        {
            if (Time.unscaledTime >= nextSourceResolveTime)
            {
                nextSourceResolveTime = Time.unscaledTime + 0.5f;
                TryResolveAudienceSources();
            }

            return;
        }

        RPC_SubmitAudiencePose(
            audienceHeadSource.position,
            audienceHeadSource.rotation,
            audienceRightHandSource.position,
            audienceRightHandSource.rotation
        );
    }

    private bool AreAudienceSourcesReady()
    {
        if (audienceHeadReader == null ||
            audienceHeadSource == null ||
            audienceRightHandSource == null ||
            !audienceHeadReader.HasValidPose)
        {
            return false;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        return audienceRightHandPose != null &&
               audienceRightHandPose.poseAction != null &&
               audienceRightHandPose.isActive &&
               audienceRightHandPose.isValid &&
               audienceRightHandPose.poseAction[
                   audienceRightHandPose.inputSource
               ].deviceIsConnected;
#else
        return false;
#endif
    }

    private void TryResolveAudienceSources()
    {
        DirectOpenVRTrackerReader[] readers =
            FindObjectsByType<DirectOpenVRTrackerReader>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        audienceHeadReader = null;
        audienceHeadSource = null;

        foreach (DirectOpenVRTrackerReader reader in readers)
        {
            if (reader == null || reader.Target == null)
                continue;

            audienceHeadReader = reader;
            audienceHeadSource = reader.Target;
            break;
        }

        audienceRightHandSource = FindTransformByExactName(
            rightControllerObjectName
        );

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        audienceRightHandPose = audienceRightHandSource != null
            ? audienceRightHandSource.GetComponent<SteamVR_Behaviour_Pose>()
            : null;
#endif

        bool foundBoth =
            audienceHeadSource != null &&
            audienceRightHandSource != null;

        if (foundBoth && !loggedSourcesReady)
        {
            loggedSourcesReady = true;
            loggedMissingSources = false;
            Debug.Log(
                "AudiencePoseNetworkHub: Audience Vive sources bound. " +
                "Head=" + audienceHeadSource.name +
                ", RightHand=" + audienceRightHandSource.name + ".",
                this
            );
        }
        else if (!foundBoth && !loggedMissingSources)
        {
            loggedMissingSources = true;
            Debug.LogWarning(
                "AudiencePoseNetworkHub: Waiting for the first " +
                "DirectOpenVRTrackerReader target and " +
                rightControllerObjectName + ".",
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

        bool poseIsFresh =
            hasReceivedPose &&
            Time.realtimeSinceStartup - lastPoseReceiveTime <=
            PoseTimeoutSeconds;

        if (!poseIsFresh ||
            actorHeadTarget == null ||
            actorRightHandTarget == null)
        {
            SetActorTargetVisibility(false);
            return;
        }

        SetActorTargetVisibility(true);

        if (snapActorTargetsOnNextUpdate)
        {
            actorHeadTarget.SetPositionAndRotation(
                receivedHeadPosition,
                receivedHeadRotation
            );
            actorRightHandTarget.SetPositionAndRotation(
                receivedRightHandPosition,
                receivedRightHandRotation
            );
            snapActorTargetsOnNextUpdate = false;
            return;
        }

        float blend = smoothingSpeed <= 0f
            ? 1f
            : 1f - Mathf.Exp(-smoothingSpeed * Time.deltaTime);

        ApplySmoothedPose(
            actorHeadTarget,
            receivedHeadPosition,
            receivedHeadRotation,
            blend
        );
        ApplySmoothedPose(
            actorRightHandTarget,
            receivedRightHandPosition,
            receivedRightHandRotation,
            blend
        );
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
        }
        else if (!foundBoth && !loggedMissingTargets)
        {
            loggedMissingTargets = true;
            Debug.LogWarning(
                "AudiencePoseNetworkHub: Waiting for actor-side " +
                "AudiencePoseVisualTarget components for Head and RightHand.",
                this
            );
        }
    }

    private void SetActorTargetVisibility(bool visible)
    {
        actorTargetsVisible = visible;

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
    private void RPC_SubmitAudiencePose(
        Vector3 headPosition,
        Quaternion headRotation,
        Vector3 rightHandPosition,
        Quaternion rightHandRotation,
        RpcInfo info = default)
    {
        if (Runner == null ||
            info.Source == PlayerRef.None ||
            info.Source == Runner.LocalPlayer ||
            info.Source != GetOnlyOtherPlayer() ||
            !IsReasonablePose(headPosition, headRotation) ||
            !IsReasonablePose(rightHandPosition, rightHandRotation))
        {
            return;
        }

        bool poseWasFresh =
            hasReceivedPose &&
            Time.realtimeSinceStartup - lastPoseReceiveTime <=
            PoseTimeoutSeconds;

        receivedHeadPosition = headPosition;
        receivedHeadRotation = headRotation.normalized;
        receivedRightHandPosition = rightHandPosition;
        receivedRightHandRotation = rightHandRotation.normalized;
        lastPoseReceiveTime = Time.realtimeSinceStartup;
        hasReceivedPose = true;

        if (!poseWasFresh || !actorTargetsVisible)
            snapActorTargetsOnNextUpdate = true;
    }

    [Rpc(
        RpcSources.StateAuthority,
        RpcTargets.All,
        Channel = RpcChannel.Reliable,
        TickAligned = false
    )]
    private void RPC_PlayAudienceClapHaptic()
    {
        // The Actor Host validates the two-hand contact. Only a proxy (the
        // Windows audience client) should play the Vive controller haptic.
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

    private static Transform FindTransformByExactName(string objectName)
    {
        Transform[] transforms = FindObjectsByType<Transform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (Transform candidate in transforms)
        {
            if (candidate != null && candidate.name == objectName)
                return candidate;
        }

        return null;
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
