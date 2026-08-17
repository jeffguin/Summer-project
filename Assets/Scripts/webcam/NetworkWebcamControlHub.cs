using System;
using System.Collections;
using Fusion;
using UnityEngine;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using Valve.VR;
#endif

public class NetworkWebcamControlHub : NetworkBehaviour
{
    private const float HighFiveRayTimeoutSeconds = 0.5f;
    private const float MaximumHighFiveRayLength = 5.5f;
    private const float TrackedMarkerTimeoutSeconds = 0.75f;
    private const float MaximumTrackedMarkerDistance = 50f;

    [Header("Video Retry")]
    [SerializeField] private bool autoRetryTransientVideoFailures = true;
    [SerializeField] private int maxAutoRetryAttempts = 2;
    [SerializeField] private float firstRetryDelaySeconds = 1f;
    [SerializeField] private float secondRetryDelaySeconds = 3f;

    [Header("Audience Tracked Markers")]
    [Tooltip("把观众端 Vive Tracker（头部）和右手 Vive 手柄姿态发送到演员端。")]
    [SerializeField] private bool enableAudienceTrackedMarkers = true;

    [Tooltip("每秒向演员端发送观众头部和手部姿态的次数。")]
    [SerializeField, Min(1f)] private float trackedMarkerSendRate = 30f;

    [Tooltip("演员端头部方块的边长（米）。")]
    [SerializeField, Min(0.01f)] private float audienceHeadCubeSize = 0.22f;

    [Tooltip("演员端手部方块的边长（米）。")]
    [SerializeField, Min(0.01f)] private float audienceHandCubeSize = 0.12f;

    [Tooltip("演员端方块跟随网络姿态的平滑速度。")]
    [SerializeField, Min(0f)] private float trackedMarkerSmoothing = 24f;

    private AudienceWebcamRuntime audienceRuntime;
    private PerformerWebcamControlPanel performerPanel;
    private WebRtcVideoReceiver actorVideoReceiver;
    private WebRtcAudioEndpoint actorAudioEndpoint;
    private PlayerRef audiencePlayer = PlayerRef.None;
    private Coroutine cameraReportCoroutine;
    private Coroutine videoRetryCoroutine;
    private bool videoStartWasRequested;
    private int videoRetryAttempt;
    private bool videoRetryAllowed;
    private LineRenderer actorHighFiveRayVisual;
    private Material actorHighFiveRayMaterial;
    private Vector3 actorHighFiveRayStart;
    private Vector3 actorHighFiveRayEnd;
    private float lastHighFiveRayReceiveTime = float.NegativeInfinity;
    private DirectOpenVRTrackerReader audienceHeadTrackerReader;
    private Transform audienceHeadPoseSource;
    private Transform audienceHandPoseSource;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private SteamVR_Behaviour_Pose audienceHandPoseReader;
#endif
    private float nextTrackedSourceResolveTime;
    private float nextTrackedMarkerSendTime;
    private bool loggedTrackedSourcesReady;
    private bool loggedMissingTrackedSources;
    private GameObject actorAudienceHeadCube;
    private GameObject actorAudienceHandCube;
    private Material actorAudienceHeadMaterial;
    private Material actorAudienceHandMaterial;
    private Vector3 actorAudienceHeadPosition;
    private Quaternion actorAudienceHeadRotation = Quaternion.identity;
    private Vector3 actorAudienceHandPosition;
    private Quaternion actorAudienceHandRotation = Quaternion.identity;
    private float lastTrackedMarkerReceiveTime = float.NegativeInfinity;
    private bool hasReceivedTrackedMarkerPose;

    public override void Spawned()
    {
        audienceRuntime = FindFirstObjectByType<AudienceWebcamRuntime>(FindObjectsInactive.Include);
        performerPanel = FindFirstObjectByType<PerformerWebcamControlPanel>(FindObjectsInactive.Include);
        actorVideoReceiver = FindFirstObjectByType<WebRtcVideoReceiver>(FindObjectsInactive.Include);
        actorAudioEndpoint = FindActorAudioEndpoint();

        if (actorVideoReceiver != null)
        {
            actorVideoReceiver.StateChanged -= OnActorVideoStateChanged;
            actorVideoReceiver.StateChanged += OnActorVideoStateChanged;
        }

        Debug.Log("NetworkWebcamControlHub spawned. LocalPlayer: " + Runner.LocalPlayer);

        if (audienceRuntime != null)
            cameraReportCoroutine = StartCoroutine(ReportCameraListWhenActorIsReady());

        if (actorVideoReceiver != null)
            OnActorVideoStateChanged(actorVideoReceiver.State, "Video receiver is ready.");

        if (audienceRuntime != null)
            TryResolveAudienceTrackedSources();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (cameraReportCoroutine != null)
        {
            StopCoroutine(cameraReportCoroutine);
            cameraReportCoroutine = null;
        }

        CancelVideoRetry();
        videoRetryAllowed = false;
        videoStartWasRequested = false;

        if (actorVideoReceiver != null)
        {
            actorVideoReceiver.StateChanged -= OnActorVideoStateChanged;
            actorVideoReceiver.StopReceiving();
        }

        if (audienceRuntime != null)
            audienceRuntime.ForceStopAudienceVideo();

        DestroyActorHighFiveRayVisual();
        DestroyActorTrackedMarkerVisuals();
    }

    private void Update()
    {
        SendAudienceTrackedMarkersWhenReady();
    }

    public override void Render()
    {
        // The Actor Host owns State Authority for this object. Rendering only
        // on that peer makes the audience ray visible to the actor without
        // duplicating it in the audience scene.
        if (Object == null || !Object.HasStateAuthority)
            return;

        bool isFresh =
            Time.realtimeSinceStartup - lastHighFiveRayReceiveTime <=
            HighFiveRayTimeoutSeconds;

        if (!isFresh)
        {
            if (actorHighFiveRayVisual != null)
                actorHighFiveRayVisual.enabled = false;
        }
        else
        {
            EnsureActorHighFiveRayVisual();

            if (actorHighFiveRayVisual != null)
            {
                actorHighFiveRayVisual.enabled = true;
                actorHighFiveRayVisual.SetPosition(0, actorHighFiveRayStart);
                actorHighFiveRayVisual.SetPosition(1, actorHighFiveRayEnd);
            }
        }

        RenderActorTrackedMarkers();
    }

    private void SendAudienceTrackedMarkersWhenReady()
    {
        if (!enableAudienceTrackedMarkers ||
            audienceRuntime == null ||
            Object == null ||
            !Object.IsValid ||
            Object.HasStateAuthority ||
            Runner == null ||
            !Runner.IsRunning ||
            Time.unscaledTime < nextTrackedMarkerSendTime)
        {
            return;
        }

        nextTrackedMarkerSendTime =
            Time.unscaledTime + 1f / Mathf.Max(1f, trackedMarkerSendRate);

        if (!AreAudienceTrackedSourcesReady())
        {
            if (Time.unscaledTime >= nextTrackedSourceResolveTime)
            {
                nextTrackedSourceResolveTime = Time.unscaledTime + 0.5f;
                TryResolveAudienceTrackedSources();
            }

            return;
        }

        RPC_SubmitAudienceTrackedMarkers(
            audienceHeadPoseSource.position,
            audienceHeadPoseSource.rotation,
            audienceHandPoseSource.position,
            audienceHandPoseSource.rotation
        );
    }

    private bool AreAudienceTrackedSourcesReady()
    {
        if (audienceHeadTrackerReader == null ||
            audienceHeadPoseSource == null ||
            audienceHandPoseSource == null ||
            !audienceHeadTrackerReader.HasValidPose)
        {
            return false;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (audienceHandPoseReader == null ||
            audienceHandPoseReader.poseAction == null ||
            !audienceHandPoseReader.isActive ||
            !audienceHandPoseReader.isValid ||
            !audienceHandPoseReader.poseAction[
                audienceHandPoseReader.inputSource
            ].deviceIsConnected)
        {
            return false;
        }
#endif

        return true;
    }

    private void TryResolveAudienceTrackedSources()
    {
        DirectOpenVRTrackerReader[] trackerReaders =
            FindObjectsByType<DirectOpenVRTrackerReader>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        audienceHeadTrackerReader = null;
        audienceHeadPoseSource = null;

        foreach (DirectOpenVRTrackerReader reader in trackerReaders)
        {
            if (reader == null || reader.Target == null)
                continue;

            audienceHeadTrackerReader = reader;
            audienceHeadPoseSource = reader.Target;
            break;
        }

        audienceHandPoseSource = FindTransformByExactName(
            "ViveRightController"
        );

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        audienceHandPoseReader = audienceHandPoseSource != null
            ? audienceHandPoseSource.GetComponent<SteamVR_Behaviour_Pose>()
            : null;
#endif

        bool hasBothSources =
            audienceHeadPoseSource != null &&
            audienceHandPoseSource != null;

        if (hasBothSources && !loggedTrackedSourcesReady)
        {
            loggedTrackedSourcesReady = true;
            loggedMissingTrackedSources = false;
            Debug.Log(
                "NetworkWebcamControlHub: Audience tracked marker sources bound. " +
                "Head=" + audienceHeadPoseSource.name +
                ", Hand=" + audienceHandPoseSource.name + "."
            );
        }
        else if (!hasBothSources && !loggedMissingTrackedSources)
        {
            loggedMissingTrackedSources = true;
            Debug.LogWarning(
                "NetworkWebcamControlHub: Waiting for Audience Vive sources. " +
                "Expected the first DirectOpenVRTrackerReader target and " +
                "the ViveRightController object."
            );
        }
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

    [Rpc(
        sources: RpcSources.All,
        targets: RpcTargets.StateAuthority,
        Channel = RpcChannel.Unreliable,
        TickAligned = false,
        HostMode = RpcHostMode.SourceIsHostPlayer
    )]
    private void RPC_SubmitAudienceTrackedMarkers(
        Vector3 headPosition,
        Quaternion headRotation,
        Vector3 handPosition,
        Quaternion handRotation,
        RpcInfo info = default)
    {
        if (Runner == null ||
            info.Source == PlayerRef.None ||
            info.Source == Runner.LocalPlayer ||
            info.Source != GetOnlyOtherPlayer(false) ||
            !IsReasonableTrackedPose(headPosition, headRotation) ||
            !IsReasonableTrackedPose(handPosition, handRotation))
        {
            return;
        }

        actorAudienceHeadPosition = headPosition;
        actorAudienceHeadRotation = headRotation.normalized;
        actorAudienceHandPosition = handPosition;
        actorAudienceHandRotation = handRotation.normalized;
        lastTrackedMarkerReceiveTime = Time.realtimeSinceStartup;
        hasReceivedTrackedMarkerPose = true;
    }

    private static bool IsReasonableTrackedPose(
        Vector3 position,
        Quaternion rotation)
    {
        return IsFinite(position) &&
               IsFinite(rotation) &&
               position.sqrMagnitude <=
               MaximumTrackedMarkerDistance * MaximumTrackedMarkerDistance &&
               Quaternion.Dot(rotation, rotation) > 0.0001f;
    }

    private void RenderActorTrackedMarkers()
    {
        bool isFresh =
            hasReceivedTrackedMarkerPose &&
            Time.realtimeSinceStartup - lastTrackedMarkerReceiveTime <=
            TrackedMarkerTimeoutSeconds;

        if (!isFresh)
        {
            SetActorTrackedMarkerVisibility(false);
            return;
        }

        EnsureActorTrackedMarkerVisuals();
        SetActorTrackedMarkerVisibility(true);

        float blend = trackedMarkerSmoothing <= 0f
            ? 1f
            : 1f - Mathf.Exp(-trackedMarkerSmoothing * Time.deltaTime);

        ApplySmoothedPose(
            actorAudienceHeadCube.transform,
            actorAudienceHeadPosition,
            actorAudienceHeadRotation,
            blend
        );
        ApplySmoothedPose(
            actorAudienceHandCube.transform,
            actorAudienceHandPosition,
            actorAudienceHandRotation,
            blend
        );
    }

    private void EnsureActorTrackedMarkerVisuals()
    {
        if (actorAudienceHeadCube == null)
        {
            actorAudienceHeadCube = CreateActorMarkerCube(
                "Audience Head (Vive Tracker, Network)",
                audienceHeadCubeSize,
                new Color(0.05f, 0.85f, 1f, 1f),
                out actorAudienceHeadMaterial
            );
            actorAudienceHeadCube.transform.SetPositionAndRotation(
                actorAudienceHeadPosition,
                actorAudienceHeadRotation
            );
        }

        if (actorAudienceHandCube == null)
        {
            actorAudienceHandCube = CreateActorMarkerCube(
                "Audience Hand (Vive Controller, Network)",
                audienceHandCubeSize,
                new Color(1f, 0.35f, 0.05f, 1f),
                out actorAudienceHandMaterial
            );
            actorAudienceHandCube.transform.SetPositionAndRotation(
                actorAudienceHandPosition,
                actorAudienceHandRotation
            );
        }
    }

    private GameObject CreateActorMarkerCube(
        string objectName,
        float size,
        Color color,
        out Material material)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = objectName;
        cube.transform.SetParent(transform, true);
        cube.transform.localScale = Vector3.one * Mathf.Max(0.01f, size);

        Collider cubeCollider = cube.GetComponent<Collider>();
        if (cubeCollider != null)
            Destroy(cubeCollider);

        Renderer cubeRenderer = cube.GetComponent<Renderer>();
        cubeRenderer.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        cubeRenderer.receiveShadows = false;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        material = shader != null
            ? new Material(shader)
            : null;

        if (material != null)
        {
            material.name = objectName + " Material";
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            cubeRenderer.material = material;
        }
        else
        {
            Debug.LogWarning(
                "NetworkWebcamControlHub: No shader was available for " +
                objectName + "."
            );
        }

        return cube;
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

    private void SetActorTrackedMarkerVisibility(bool visible)
    {
        if (actorAudienceHeadCube != null &&
            actorAudienceHeadCube.activeSelf != visible)
        {
            actorAudienceHeadCube.SetActive(visible);
        }

        if (actorAudienceHandCube != null &&
            actorAudienceHandCube.activeSelf != visible)
        {
            actorAudienceHandCube.SetActive(visible);
        }
    }

    private void DestroyActorTrackedMarkerVisuals()
    {
        if (actorAudienceHeadCube != null)
        {
            Destroy(actorAudienceHeadCube);
            actorAudienceHeadCube = null;
        }

        if (actorAudienceHandCube != null)
        {
            Destroy(actorAudienceHandCube);
            actorAudienceHandCube = null;
        }

        if (actorAudienceHeadMaterial != null)
        {
            Destroy(actorAudienceHeadMaterial);
            actorAudienceHeadMaterial = null;
        }

        if (actorAudienceHandMaterial != null)
        {
            Destroy(actorAudienceHandMaterial);
            actorAudienceHandMaterial = null;
        }
    }

    /// <summary>
    /// Called by the Windows audience client at a throttled rate. The RPC is
    /// unreliable because a newer ray pose always supersedes an older one.
    /// </summary>
    public void SubmitAudienceHighFiveRay(Vector3 start, Vector3 end)
    {
        if (Object == null ||
            !Object.IsValid ||
            Runner == null ||
            !Runner.IsRunning)
        {
            return;
        }

        RPC_SubmitAudienceHighFiveRay(start, end);
    }

    [Rpc(
        sources: RpcSources.All,
        targets: RpcTargets.StateAuthority,
        Channel = RpcChannel.Unreliable,
        TickAligned = false,
        HostMode = RpcHostMode.SourceIsHostPlayer
    )]
    private void RPC_SubmitAudienceHighFiveRay(
        Vector3 start,
        Vector3 end,
        RpcInfo info = default)
    {
        if (Runner == null ||
            info.Source == PlayerRef.None ||
            info.Source == Runner.LocalPlayer ||
            info.Source != GetOnlyOtherPlayer(false) ||
            !IsFinite(start) ||
            !IsFinite(end))
        {
            return;
        }

        Vector3 direction = end - start;
        float length = direction.magnitude;

        if (length < 0.01f)
            return;

        if (length > MaximumHighFiveRayLength)
        {
            end = start +
                  direction / length * MaximumHighFiveRayLength;
        }

        actorHighFiveRayStart = start;
        actorHighFiveRayEnd = end;
        lastHighFiveRayReceiveTime = Time.realtimeSinceStartup;
    }

    private void EnsureActorHighFiveRayVisual()
    {
        if (actorHighFiveRayVisual != null)
            return;

        GameObject visualObject = new GameObject(
            "AudienceHighFiveRay (Network)"
        );
        visualObject.transform.SetParent(transform, false);

        actorHighFiveRayVisual =
            visualObject.AddComponent<LineRenderer>();
        actorHighFiveRayVisual.useWorldSpace = true;
        actorHighFiveRayVisual.positionCount = 2;
        actorHighFiveRayVisual.startWidth = 0.018f;
        actorHighFiveRayVisual.endWidth = 0.018f;
        actorHighFiveRayVisual.startColor =
            new Color(0.15f, 0.85f, 1f, 0.95f);
        actorHighFiveRayVisual.endColor =
            new Color(0.05f, 0.35f, 1f, 0.65f);
        actorHighFiveRayVisual.numCapVertices = 4;
        actorHighFiveRayVisual.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        actorHighFiveRayVisual.receiveShadows = false;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader != null)
        {
            actorHighFiveRayMaterial = new Material(shader)
            {
                name = "AudienceHighFiveRay Runtime Material"
            };
            actorHighFiveRayVisual.material = actorHighFiveRayMaterial;
        }
        else
        {
            Debug.LogWarning(
                "NetworkWebcamControlHub: No unlit shader was available for the actor-side high-five ray."
            );
        }
    }

    private void DestroyActorHighFiveRayVisual()
    {
        if (actorHighFiveRayVisual != null)
        {
            Destroy(actorHighFiveRayVisual.gameObject);
            actorHighFiveRayVisual = null;
        }

        if (actorHighFiveRayMaterial != null)
        {
            Destroy(actorHighFiveRayMaterial);
            actorHighFiveRayMaterial = null;
        }
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

    private IEnumerator ReportCameraListWhenActorIsReady()
    {
        yield return new WaitForSecondsRealtime(1f);

        float deadline = Time.realtimeSinceStartup + 30f;
        while (Runner != null &&
               GetOnlyOtherPlayer(false) == PlayerRef.None &&
               Time.realtimeSinceStartup < deadline)
        {
            yield return new WaitForSecondsRealtime(0.5f);
        }

        ReportLocalAudienceCameraList();
        cameraReportCoroutine = null;
    }

    private void ReportLocalAudienceCameraList()
    {
        if (audienceRuntime == null || Runner == null)
        {
            Debug.LogWarning("NetworkWebcamControlHub: Audience runtime or Runner is missing.");
            return;
        }

        PlayerRef actorPlayer = GetOnlyOtherPlayer();
        if (actorPlayer == PlayerRef.None)
        {
            Debug.LogWarning("NetworkWebcamControlHub: Cannot report cameras without an Actor player.");
            return;
        }

        string[] names = audienceRuntime.GetCameraNames();
        RPC_ReportCameraList(actorPlayer, string.Join("\n", names));
    }

    public void RequestStartAllAudienceVideo()
    {
        CancelVideoRetry();
        videoStartWasRequested = true;
        videoRetryAttempt = 0;
        videoRetryAllowed = true;
        StartAudienceVideoSession();
    }

    // Compatibility entry point for older serialized UI events.
    public void RequestStartAudienceVideo(int cameraIndex)
    {
        RequestStartAllAudienceVideo();
    }

    private void StartAudienceVideoSession()
    {
        performerPanel ??=
            FindFirstObjectByType<PerformerWebcamControlPanel>(FindObjectsInactive.Include);
        actorVideoReceiver ??=
            FindFirstObjectByType<WebRtcVideoReceiver>(FindObjectsInactive.Include);

        if (performerPanel == null || actorVideoReceiver == null)
        {
            Debug.LogWarning(
                "NetworkWebcamControlHub: Only the Actor client with a video receiver can start video."
            );
            return;
        }

        PlayerRef target = GetAudiencePlayer();
        if (target == PlayerRef.None)
        {
            Debug.LogWarning("NetworkWebcamControlHub: Audience player is unavailable.");
            return;
        }

        string sessionId = Guid.NewGuid().ToString("N");
        if (!actorVideoReceiver.PrepareSession(sessionId, target))
            return;

        Debug.Log(
            "NetworkWebcamControlHub: Starting all Audience cameras. Session: " + sessionId +
            ", Target: " + target
        );

        RPC_StartAudienceVideo(target, sessionId);
    }

    public void RequestStopAudienceVideo()
    {
        videoRetryAllowed = false;
        videoStartWasRequested = false;
        CancelVideoRetry();

        actorVideoReceiver ??=
            FindFirstObjectByType<WebRtcVideoReceiver>(FindObjectsInactive.Include);

        if (actorVideoReceiver != null && !string.IsNullOrEmpty(actorVideoReceiver.ActiveSessionId))
        {
            actorVideoReceiver.RequestStopReceiving();
            return;
        }

        // A stale sender may exist even when the Actor receiver has already failed.
        // This force-stop fallback is still targeted and source-validated.
        PlayerRef target = GetAudiencePlayer();
        if (target != PlayerRef.None)
            RPC_ForceStopAudienceVideo(target);
    }

    [Rpc(
        sources: RpcSources.All,
        targets: RpcTargets.All,
        TickAligned = false,
        HostMode = RpcHostMode.SourceIsHostPlayer
    )]
    private void RPC_ReportCameraList(
        PlayerRef target,
        string joinedNames,
        RpcInfo info = default)
    {
        if (Runner == null || Runner.LocalPlayer != target || info.Source == PlayerRef.None)
            return;

        PlayerRef expectedAudience = GetOnlyOtherPlayer();
        if (expectedAudience == PlayerRef.None || info.Source != expectedAudience)
        {
            Debug.LogWarning(
                "NetworkWebcamControlHub: Rejected a camera list from unexpected player " +
                info.Source + "."
            );
            return;
        }

        PerformerWebcamControlPanel panel =
            FindFirstObjectByType<PerformerWebcamControlPanel>(FindObjectsInactive.Include);

        if (panel == null)
            return;

        audiencePlayer = info.Source;

        string[] names = string.IsNullOrEmpty(joinedNames)
            ? Array.Empty<string>()
            : joinedNames.Split('\n');

        panel.SetCameraList(names);
        Debug.Log(
            "NetworkWebcamControlHub: Received " + names.Length +
            " cameras from Audience player " + audiencePlayer + "."
        );
    }

    [Rpc(
        sources: RpcSources.All,
        targets: RpcTargets.All,
        TickAligned = false,
        HostMode = RpcHostMode.SourceIsHostPlayer
    )]
    private void RPC_StartAudienceVideo(
        PlayerRef target,
        string sessionId,
        RpcInfo info = default)
    {
        if (!IsAuthorizedAudienceCommand(target, info.Source))
            return;

        AudienceWebcamRuntime runtime =
            FindFirstObjectByType<AudienceWebcamRuntime>(FindObjectsInactive.Include);

        if (runtime == null)
            return;

        runtime.StartAudienceVideo(sessionId, info.Source);
    }

    [Rpc(
        sources: RpcSources.All,
        targets: RpcTargets.All,
        TickAligned = false,
        HostMode = RpcHostMode.SourceIsHostPlayer
    )]
    private void RPC_ForceStopAudienceVideo(
        PlayerRef target,
        RpcInfo info = default)
    {
        if (!IsAuthorizedAudienceCommand(target, info.Source))
            return;

        AudienceWebcamRuntime runtime =
            FindFirstObjectByType<AudienceWebcamRuntime>(FindObjectsInactive.Include);

        if (runtime != null)
            runtime.ForceStopAudienceVideo();
    }

    private bool IsAuthorizedAudienceCommand(PlayerRef target, PlayerRef source)
    {
        if (Runner == null ||
            Runner.LocalPlayer != target ||
            source == PlayerRef.None ||
            source == Runner.LocalPlayer)
        {
            return false;
        }

        PlayerRef expectedActor = GetOnlyOtherPlayer();
        if (expectedActor == PlayerRef.None || source != expectedActor)
        {
            Debug.LogWarning(
                "NetworkWebcamControlHub: Rejected a camera command from unexpected player " + source + "."
            );
            return false;
        }

        return true;
    }

    private PlayerRef GetAudiencePlayer()
    {
        if (audiencePlayer != PlayerRef.None)
            return audiencePlayer;

        audiencePlayer = GetOnlyOtherPlayer();
        return audiencePlayer;
    }

    private PlayerRef GetOnlyOtherPlayer(bool logWarning = true)
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

        if (count != 1)
        {
            if (logWarning)
            {
                Debug.LogWarning(
                    "NetworkWebcamControlHub: Video control requires exactly one remote player. " +
                    "Remote count: " + count + "."
                );
            }
            return PlayerRef.None;
        }

        return result;
    }

    private void OnActorVideoStateChanged(WebRtcVideoReceiver.SessionState state, string message)
    {
        performerPanel ??=
            FindFirstObjectByType<PerformerWebcamControlPanel>(FindObjectsInactive.Include);

        if (performerPanel != null)
            performerPanel.SetVideoState(state, message);

        if (state == WebRtcVideoReceiver.SessionState.Connected)
        {
            videoRetryAttempt = 0;
            return;
        }

        if (state == WebRtcVideoReceiver.SessionState.Failed)
            TryScheduleVideoRetry(message);
    }

    private void TryScheduleVideoRetry(string failureMessage)
    {
        if (!autoRetryTransientVideoFailures ||
            !videoRetryAllowed ||
            !videoStartWasRequested ||
            videoRetryCoroutine != null ||
            videoRetryAttempt >= Mathf.Max(0, maxAutoRetryAttempts) ||
            !IsTransientVideoFailure(failureMessage))
        {
            return;
        }

        videoRetryAttempt++;
        float delay = videoRetryAttempt == 1
            ? Mathf.Max(0.1f, firstRetryDelaySeconds)
            : Mathf.Max(0.1f, secondRetryDelaySeconds);

        if (performerPanel != null)
        {
            performerPanel.SetVideoState(
                WebRtcVideoReceiver.SessionState.Recovering,
                "Automatic retry " + videoRetryAttempt + "/" +
                Mathf.Max(0, maxAutoRetryAttempts) + " in " + delay + " seconds."
            );
        }

        videoRetryCoroutine = StartCoroutine(VideoRetryRoutine(delay));
    }

    private IEnumerator VideoRetryRoutine(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        videoRetryCoroutine = null;

        if (!videoRetryAllowed)
            yield break;

        StartAudienceVideoSession();
    }

    private void CancelVideoRetry()
    {
        if (videoRetryCoroutine == null)
            return;

        StopCoroutine(videoRetryCoroutine);
        videoRetryCoroutine = null;
    }

    private static bool IsTransientVideoFailure(string message)
    {
        if (string.IsNullOrEmpty(message))
            return false;

        return message.StartsWith("IceFailed:", StringComparison.Ordinal) ||
               message.StartsWith("PeerConnectionFailed:", StringComparison.Ordinal) ||
               message.StartsWith("ConnectionLost:", StringComparison.Ordinal) ||
               message.StartsWith("ConnectionTimeout:", StringComparison.Ordinal) ||
               message.StartsWith("RemoteFrameTimeout:", StringComparison.Ordinal) ||
               message.StartsWith("CameraFrameStalled:", StringComparison.Ordinal);
    }

    // Audio control remains a small Fusion-backed control plane. The actual
    // SDP/ICE exchange and media are owned by WebRtcAudioEndpoint.
    public void RequestAudienceMicrophoneList()
    {
        WebRtcAudioEndpoint endpoint = GetActorAudioEndpoint();
        if (endpoint == null || !endpoint.RequestAudienceMicrophoneList())
            Debug.LogWarning("NetworkWebcamControlHub: Could not request the Audience microphone list.");
    }

    public void RequestSelectAudienceMicrophone(string deviceName)
    {
        WebRtcAudioEndpoint endpoint = GetActorAudioEndpoint();
        if (endpoint == null || !endpoint.SelectAudienceMicrophone(deviceName))
            Debug.LogWarning("NetworkWebcamControlHub: Could not select the Audience microphone.");
    }

    public void RequestStartAudienceAudio()
    {
        WebRtcAudioEndpoint endpoint = GetActorAudioEndpoint();
        if (endpoint == null)
        {
            Debug.LogWarning("NetworkWebcamControlHub: Actor audio endpoint is missing.");
            return;
        }

        endpoint.StartAudioSession();
    }

    public void RequestStopAudienceAudio()
    {
        WebRtcAudioEndpoint endpoint = GetActorAudioEndpoint();
        if (endpoint == null)
        {
            Debug.LogWarning("NetworkWebcamControlHub: Actor audio endpoint is missing.");
            return;
        }

        endpoint.StopAudioSession();
    }

    public WebRtcAudioEndpoint GetActorAudioEndpoint()
    {
        if (actorAudioEndpoint == null)
            actorAudioEndpoint = FindActorAudioEndpoint();

        return actorAudioEndpoint;
    }

    private static WebRtcAudioEndpoint FindActorAudioEndpoint()
    {
        WebRtcAudioEndpoint[] endpoints =
            FindObjectsByType<WebRtcAudioEndpoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (WebRtcAudioEndpoint endpoint in endpoints)
        {
            if (endpoint.Role == WebRtcAudioEndpoint.EndpointRole.Actor)
                return endpoint;
        }

        return null;
    }
}
