using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using Unity.WebRTC;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WebRtcVideoReceiver : MonoBehaviour
{
    public enum SessionState
    {
        Idle,
        WaitingForSignalHub,
        Negotiating,
        Connecting,
        Connected,
        Recovering,
        Stopping,
        Failed
    }

    [Header("Display")]
    [SerializeField] private VideoDisplayScreen videoDisplayScreen;

    [Header("ICE / STUN / TURN Settings")]
    [SerializeField] private bool useGoogleStun = true;
    [SerializeField] private bool useTurn = false;
    [SerializeField] private string turnUrlUdp = "turn:YOUR_TURN_SERVER:3478?transport=udp";
    [SerializeField] private string turnUrlTcp = "turn:YOUR_TURN_SERVER:3478?transport=tcp";
    [SerializeField] private string turnUsername = "YOUR_USERNAME";
    [SerializeField] private string turnCredential = "YOUR_PASSWORD";

    [Header("Timeouts")]
    [SerializeField] private float connectionTimeoutSeconds = 20f;
    [SerializeField] private float remoteFrameTimeoutSeconds = 5f;
    [SerializeField] private float disconnectedGraceSeconds = 3f;
    [SerializeField] private float stopAckTimeoutSeconds = 2f;

    private RTCPeerConnection peerConnection;
    private Coroutine signalHubCoroutine;
    private Coroutine operationCoroutine;
    private Coroutine connectionTimeoutCoroutine;
    private Coroutine disconnectedCoroutine;
    private Coroutine stopAckCoroutine;

    private bool signalHubSubscribed;
    private bool remoteDescriptionSet;
    private bool iceConnected;
    private bool firstFrameReceived;
    private bool connectedSignalSent;
    private bool isCleaningUp;
    private string activeSessionId = "";
    private PlayerRef remotePlayer = PlayerRef.None;

    private readonly Queue<IceSignal> pendingRemoteCandidates = new Queue<IceSignal>();
    private readonly List<VideoDisplayScreen> displayScreens = new List<VideoDisplayScreen>();
    private readonly List<VideoStreamDescriptor> availableStreams = new List<VideoStreamDescriptor>();
    private readonly Dictionary<string, RemoteVideoStream> remoteStreams =
        new Dictionary<string, RemoteVideoStream>();

    public event Action<SessionState, string> StateChanged;
    public event Action AvailableStreamsChanged;
    public event Action DisplayScreensChanged;

    public SessionState State { get; private set; } = SessionState.Idle;
    public string ActiveSessionId => activeSessionId;
    public PlayerRef RemotePlayer => remotePlayer;
    public bool IsConnected => State == SessionState.Connected;
    public IReadOnlyList<VideoStreamDescriptor> AvailableStreams => availableStreams;
    public IReadOnlyList<VideoDisplayScreen> DisplayScreens => displayScreens;

    [Serializable]
    private sealed class SessionSignal
    {
        public string sessionId;
        public string sdp;
        public string errorCode;
        public string message;
        public VideoStreamDescriptor[] tracks;
    }

    private sealed class RemoteVideoStream
    {
        public VideoStreamDescriptor descriptor;
        public VideoStreamTrack track;
        public OnVideoReceived videoHandler;
        public Texture texture;
        public float lastFrameTime;
        public bool firstFrameReceived;
    }

    [Serializable]
    private sealed class IceSignal
    {
        public string sessionId;
        public string candidate;
        public string sdpMid;
        public int sdpMLineIndex;
    }

    private void Awake()
    {
        WebRtcRuntimePump.EnsureExists();
        RefreshDisplayScreens();
    }

    private void OnEnable()
    {
        VideoDisplayScreen.RegistryChanged -= OnDisplayScreenRegistryChanged;
        VideoDisplayScreen.RegistryChanged += OnDisplayScreenRegistryChanged;
        RefreshDisplayScreens();

        if (signalHubCoroutine == null)
            signalHubCoroutine = StartCoroutine(WaitForSignalHub());
    }

    private void Update()
    {
        if (State != SessionState.Connected)
            return;

        float now = Time.realtimeSinceStartup;
        foreach (RemoteVideoStream stream in remoteStreams.Values)
        {
            if (!stream.firstFrameReceived || stream.lastFrameTime <= 0f)
                continue;

            if (now - stream.lastFrameTime > Mathf.Max(1f, remoteFrameTimeoutSeconds))
            {
                Fail(
                    "RemoteFrameTimeout",
                    "Camera '" + stream.descriptor.deviceName +
                    "' stopped delivering video frames.",
                    true
                );
                return;
            }
        }
    }

    private void OnDisplayScreenRegistryChanged()
    {
        RefreshDisplayScreens();
    }

    public void RefreshDisplayScreens()
    {
        VideoDisplayScreen[] found =
            FindObjectsByType<VideoDisplayScreen>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        displayScreens.Clear();
        if (videoDisplayScreen != null && Array.IndexOf(found, videoDisplayScreen) < 0)
            displayScreens.Add(videoDisplayScreen);

        displayScreens.AddRange(found);
        displayScreens.RemoveAll(screen => screen == null || !screen.isActiveAndEnabled);
        displayScreens.Sort((left, right) =>
            string.Compare(left.ScreenId, right.ScreenId, StringComparison.Ordinal));

        AssignDefaultStreamsToUnassignedScreens();
        ApplyAllTexturesToDisplays();
        DisplayScreensChanged?.Invoke();

        if (displayScreens.Count == 0)
            Debug.LogWarning("WebRtcVideoReceiver: No VideoDisplayScreen is available.");
    }

    private IEnumerator WaitForSignalHub()
    {
        SetState(SessionState.WaitingForSignalHub, "Waiting for the Fusion WebRTC signal hub.");

        while (WebRtcSignalHub.Instance == null)
            yield return null;

        SubscribeToSignalHub();
        signalHubCoroutine = null;

        if (State == SessionState.WaitingForSignalHub)
            SetState(SessionState.Idle, "Video receiver is ready.");
    }

    private void SubscribeToSignalHub()
    {
        if (WebRtcSignalHub.Instance == null)
            return;

        WebRtcSignalHub.Instance.OnSignalReceived -= OnSignalReceived;
        WebRtcSignalHub.Instance.OnSignalReceived += OnSignalReceived;
        signalHubSubscribed = true;
    }

    public bool PrepareSession(string sessionId, PlayerRef expectedSender)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || expectedSender == PlayerRef.None)
        {
            Debug.LogWarning("WebRtcVideoReceiver: Cannot prepare a session without an id and sender.");
            return false;
        }

        if (!signalHubSubscribed && WebRtcSignalHub.Instance != null)
            SubscribeToSignalHub();

        if (!string.IsNullOrEmpty(activeSessionId) && remotePlayer != PlayerRef.None)
        {
            SendJson(
                remotePlayer,
                WebRtcWebcamSender.StopType,
                JsonUtility.ToJson(new SessionSignal { sessionId = activeSessionId })
            );
        }

        CleanupLocalSession("Preparing a new video receive session.", false);
        activeSessionId = sessionId;
        remotePlayer = expectedSender;
        SetState(SessionState.Negotiating, "Waiting for the Audience video offer.");
        StartConnectionTimeout(sessionId);
        return true;
    }

    private void OnSignalReceived(PlayerRef from, string type, string payload)
    {
        if (type == WebRtcWebcamSender.OfferType)
        {
            SessionSignal signal = JsonUtility.FromJson<SessionSignal>(payload);
            HandleOfferSignal(from, signal);
            return;
        }

        if (from != remotePlayer || string.IsNullOrEmpty(activeSessionId))
            return;

        if (type == WebRtcWebcamSender.CandidateType)
        {
            HandleRemoteCandidate(JsonUtility.FromJson<IceSignal>(payload));
        }
        else if (type == WebRtcWebcamSender.StopType)
        {
            SessionSignal signal = JsonUtility.FromJson<SessionSignal>(payload);
            if (MatchesActiveSession(signal))
                HandleRemoteStop(from, signal);
        }
        else if (type == WebRtcWebcamSender.StopAckType)
        {
            SessionSignal signal = JsonUtility.FromJson<SessionSignal>(payload);
            if (State == SessionState.Stopping && MatchesActiveSession(signal))
                CleanupLocalSession("Audience acknowledged the video stop.");
        }
        else if (type == WebRtcWebcamSender.ErrorType)
        {
            SessionSignal signal = JsonUtility.FromJson<SessionSignal>(payload);
            if (MatchesActiveSession(signal))
                Fail(signal.errorCode ?? "RemoteVideoError", signal.message ?? "Audience video failed.", false);
        }
    }

    private void HandleOfferSignal(PlayerRef from, SessionSignal signal)
    {
        if (signal == null || string.IsNullOrEmpty(signal.sessionId) || string.IsNullOrEmpty(signal.sdp))
        {
            Debug.LogWarning("WebRtcVideoReceiver: Invalid video offer payload.");
            return;
        }

        // The NetworkWebcamControlHub normally prepares the expected session first.
        // The fallback keeps the optional sender-side test button usable in a two-player room.
        if (string.IsNullOrEmpty(activeSessionId))
        {
            PlayerRef expectedPlayer =
                WebRtcSignalHub.Instance != null
                    ? WebRtcSignalHub.Instance.GetOtherPlayer()
                    : PlayerRef.None;

            if (expectedPlayer == PlayerRef.None || from != expectedPlayer)
            {
                Debug.LogWarning("WebRtcVideoReceiver: Rejected an offer from an unexpected player.");
                return;
            }

            activeSessionId = signal.sessionId;
            remotePlayer = from;
            SetState(SessionState.Negotiating, "Accepted a video offer from the expected Audience player.");
            StartConnectionTimeout(activeSessionId);
        }

        if (from != remotePlayer || signal.sessionId != activeSessionId)
        {
            Debug.LogWarning("WebRtcVideoReceiver: Rejected a stale or mismatched video offer.");
            return;
        }

        if (operationCoroutine != null)
        {
            Debug.LogWarning("WebRtcVideoReceiver: An offer is already being processed.");
            return;
        }

        operationCoroutine = StartCoroutine(HandleOfferRoutine(signal));
    }

    private IEnumerator HandleOfferRoutine(SessionSignal signal)
    {
        ApplyStreamDescriptors(signal.tracks);

        if (!CreatePeerConnection())
        {
            operationCoroutine = null;
            Fail("PeerConnectionCreationFailed", "Could not create the receiving PeerConnection.", true);
            yield break;
        }

        RTCSessionDescription offer = new RTCSessionDescription
        {
            type = RTCSdpType.Offer,
            sdp = signal.sdp
        };

        RTCSetSessionDescriptionAsyncOperation remoteOperation =
            peerConnection.SetRemoteDescription(ref offer);
        yield return remoteOperation;

        if (!MatchesActiveSession(signal))
        {
            operationCoroutine = null;
            yield break;
        }

        if (remoteOperation.IsError)
        {
            operationCoroutine = null;
            Fail("SetRemoteOfferFailed", remoteOperation.Error.message, true);
            yield break;
        }

        remoteDescriptionSet = true;
        FlushPendingRemoteCandidates();

        RTCSessionDescriptionAsyncOperation answerOperation = peerConnection.CreateAnswer();
        yield return answerOperation;

        if (!MatchesActiveSession(signal))
        {
            operationCoroutine = null;
            yield break;
        }

        if (answerOperation.IsError)
        {
            operationCoroutine = null;
            Fail("CreateAnswerFailed", answerOperation.Error.message, true);
            yield break;
        }

        RTCSessionDescription answer = answerOperation.Desc;
        RTCSetSessionDescriptionAsyncOperation localOperation =
            peerConnection.SetLocalDescription(ref answer);
        yield return localOperation;

        if (!MatchesActiveSession(signal))
        {
            operationCoroutine = null;
            yield break;
        }

        if (localOperation.IsError)
        {
            operationCoroutine = null;
            Fail("SetLocalAnswerFailed", localOperation.Error.message, true);
            yield break;
        }

        SendJson(
            remotePlayer,
            WebRtcWebcamSender.AnswerType,
            JsonUtility.ToJson(new SessionSignal
            {
                sessionId = activeSessionId,
                sdp = answer.sdp
            })
        );

        operationCoroutine = null;
        SetState(SessionState.Connecting, "Video answer sent; waiting for ICE and the first frame.");
    }

    private bool CreatePeerConnection()
    {
        if (peerConnection != null)
            return true;

        try
        {
            RTCConfiguration configuration = BuildRtcConfiguration();
            peerConnection = new RTCPeerConnection(ref configuration);
            ConfigurePeerConnectionCallbacks();
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError("WebRtcVideoReceiver: PeerConnection creation failed: " + exception);
            return false;
        }
    }

    private void ConfigurePeerConnectionCallbacks()
    {
        peerConnection.OnIceCandidate = candidate =>
        {
            if (candidate == null ||
                string.IsNullOrEmpty(candidate.Candidate) ||
                string.IsNullOrEmpty(activeSessionId) ||
                remotePlayer == PlayerRef.None)
            {
                return;
            }

            LogCandidateType("WebRtcVideoReceiver", candidate.Candidate);

            IceSignal signal = new IceSignal
            {
                sessionId = activeSessionId,
                candidate = candidate.Candidate,
                sdpMid = candidate.SdpMid,
                sdpMLineIndex = candidate.SdpMLineIndex.HasValue
                    ? candidate.SdpMLineIndex.Value
                    : -1
            };

            SendJson(
                remotePlayer,
                WebRtcWebcamSender.CandidateType,
                JsonUtility.ToJson(signal)
            );
        };

        peerConnection.OnTrack = trackEvent =>
        {
            if (trackEvent.Track is VideoStreamTrack videoTrack)
            {
                string mid = trackEvent.Transceiver != null
                    ? trackEvent.Transceiver.Mid
                    : "";
                AttachRemoteVideoTrack(videoTrack, mid);
            }
            else
                Debug.LogWarning("WebRtcVideoReceiver: Received a non-video track.");
        };

        peerConnection.OnIceConnectionChange = state =>
        {
            if (isCleaningUp)
                return;

            Debug.Log("WebRtcVideoReceiver: ICE state = " + state + ", Session = " + activeSessionId);

            if (state == RTCIceConnectionState.Connected || state == RTCIceConnectionState.Completed)
            {
                iceConnected = true;
                CancelDisconnectedGracePeriod();
                TryMarkConnected();
            }
            else if (state == RTCIceConnectionState.Disconnected)
            {
                iceConnected = false;
                SetState(SessionState.Recovering, "Video ICE disconnected; waiting for recovery.");
                StartDisconnectedGracePeriod(activeSessionId);
            }
            else if (state == RTCIceConnectionState.Failed)
            {
                iceConnected = false;
                Fail("IceFailed", "Video ICE connection failed.", true);
            }
        };

        peerConnection.OnConnectionStateChange = state =>
        {
            if (isCleaningUp)
                return;

            Debug.Log(
                "WebRtcVideoReceiver: PeerConnection state = " + state +
                ", Session = " + activeSessionId
            );

            if (state == RTCPeerConnectionState.Failed)
                Fail("PeerConnectionFailed", "Video PeerConnection entered the Failed state.", true);
        };
    }

    private void ApplyStreamDescriptors(VideoStreamDescriptor[] descriptors)
    {
        availableStreams.Clear();
        remoteStreams.Clear();

        if (descriptors == null || descriptors.Length == 0)
        {
            descriptors = new[]
            {
                new VideoStreamDescriptor
                {
                    streamId = "camera-0",
                    cameraIndex = 0,
                    deviceName = "Audience Camera",
                    trackId = "",
                    mid = ""
                }
            };
        }

        for (int i = 0; i < descriptors.Length; i++)
        {
            VideoStreamDescriptor descriptor = descriptors[i];
            if (descriptor == null)
                continue;

            VideoStreamDescriptor copy = descriptor.Clone();
            if (string.IsNullOrEmpty(copy.streamId))
                copy.streamId = "camera-" + Mathf.Max(0, copy.cameraIndex);
            if (string.IsNullOrEmpty(copy.deviceName))
                copy.deviceName = "Audience Camera " + (i + 1);

            if (remoteStreams.ContainsKey(copy.streamId))
            {
                Debug.LogWarning(
                    "WebRtcVideoReceiver: Ignoring duplicate stream id '" + copy.streamId + "'."
                );
                continue;
            }

            availableStreams.Add(copy);
            remoteStreams.Add(copy.streamId, new RemoteVideoStream { descriptor = copy });
        }

        AssignDefaultStreamsToUnassignedScreens();
        AvailableStreamsChanged?.Invoke();
    }

    private void AttachRemoteVideoTrack(VideoStreamTrack videoTrack, string mid)
    {
        if (videoTrack == null)
            return;

        RemoteVideoStream stream = ResolveRemoteStream(videoTrack.Id, mid);
        if (stream == null)
        {
            Debug.LogWarning(
                "WebRtcVideoReceiver: Could not map remote track '" + videoTrack.Id +
                "' (MID '" + mid + "') to a camera descriptor."
            );
            videoTrack.Dispose();
            return;
        }

        if (stream.track != null && stream.track != videoTrack)
        {
            if (stream.videoHandler != null)
                stream.track.OnVideoReceived -= stream.videoHandler;
            stream.track.Dispose();
        }

        stream.track = videoTrack;
        RemoteVideoStream capturedStream = stream;
        stream.videoHandler = texture => OnRemoteVideoReceived(capturedStream, texture);
        stream.track.OnVideoReceived += stream.videoHandler;

        SetState(
            SessionState.Connecting,
            "Remote track received for '" + stream.descriptor.deviceName +
            "'; waiting for its first frame."
        );
    }

    private RemoteVideoStream ResolveRemoteStream(string trackId, string mid)
    {
        RemoteVideoStream fallback = null;

        foreach (RemoteVideoStream stream in remoteStreams.Values)
        {
            if (stream.track != null)
                continue;

            if (!string.IsNullOrEmpty(mid) && stream.descriptor.mid == mid)
                return stream;

            if (!string.IsNullOrEmpty(trackId) && stream.descriptor.trackId == trackId)
                return stream;

            fallback ??= stream;
        }

        return fallback;
    }

    private void OnRemoteVideoReceived(RemoteVideoStream stream, Texture texture)
    {
        if (stream == null || texture == null || texture.width <= 0 || texture.height <= 0)
            return;

        stream.lastFrameTime = Time.realtimeSinceStartup;
        bool textureChanged = stream.texture != texture;
        stream.texture = texture;

        if (textureChanged)
            ApplyTextureToSelectedDisplays(stream.descriptor.streamId, texture);

        if (!stream.firstFrameReceived)
        {
            stream.firstFrameReceived = true;
            Debug.Log(
                "WebRtcVideoReceiver: First remote frame received for '" +
                stream.descriptor.deviceName + "'. Size = " +
                texture.width + "x" + texture.height
            );
        }

        if (!firstFrameReceived)
            firstFrameReceived = true;

        TryMarkConnected();
    }

    private void TryMarkConnected()
    {
        if (!iceConnected || !firstFrameReceived || string.IsNullOrEmpty(activeSessionId))
            return;

        if (!connectedSignalSent)
        {
            connectedSignalSent = SendJson(
                remotePlayer,
                WebRtcWebcamSender.ConnectedType,
                JsonUtility.ToJson(new SessionSignal { sessionId = activeSessionId })
            );
        }

        CancelConnectionTimeout();

        if (State != SessionState.Connected)
            SetState(SessionState.Connected, "ICE connected and the first remote video frame is visible.");
    }

    public void SetScreenStream(VideoDisplayScreen screen, string streamId)
    {
        if (screen == null)
            return;

        screen.SelectStream(streamId);

        if (!string.IsNullOrEmpty(streamId) &&
            remoteStreams.TryGetValue(streamId, out RemoteVideoStream stream) &&
            stream.texture != null)
        {
            screen.SetTexture(stream.texture);
        }
        else
        {
            screen.ClearTexture();
        }

        Debug.Log(
            "WebRtcVideoReceiver: Screen '" + screen.DisplayName +
            "' selected stream '" + streamId + "'."
        );
    }

    private void AssignDefaultStreamsToUnassignedScreens()
    {
        if (availableStreams.Count == 0)
            return;

        for (int i = 0; i < displayScreens.Count; i++)
        {
            VideoDisplayScreen screen = displayScreens[i];
            if (screen == null)
                continue;

            bool selectionExists = false;
            for (int streamIndex = 0; streamIndex < availableStreams.Count; streamIndex++)
            {
                if (availableStreams[streamIndex].streamId == screen.SelectedStreamId)
                {
                    selectionExists = true;
                    break;
                }
            }

            if (!selectionExists)
            {
                VideoStreamDescriptor defaultStream = availableStreams[i % availableStreams.Count];
                screen.SelectStream(defaultStream.streamId);
            }
        }
    }

    private void ApplyAllTexturesToDisplays()
    {
        for (int i = 0; i < displayScreens.Count; i++)
        {
            VideoDisplayScreen screen = displayScreens[i];
            if (screen == null)
                continue;

            if (!string.IsNullOrEmpty(screen.SelectedStreamId) &&
                remoteStreams.TryGetValue(screen.SelectedStreamId, out RemoteVideoStream stream) &&
                stream.texture != null)
            {
                screen.SetTexture(stream.texture);
            }
            else
            {
                screen.ClearTexture();
            }
        }
    }

    private void ApplyTextureToSelectedDisplays(string streamId, Texture texture)
    {
        for (int i = 0; i < displayScreens.Count; i++)
        {
            VideoDisplayScreen screen = displayScreens[i];
            if (screen != null && screen.SelectedStreamId == streamId)
                screen.SetTexture(texture);
        }
    }

    private void HandleRemoteCandidate(IceSignal signal)
    {
        if (signal == null ||
            string.IsNullOrEmpty(signal.candidate) ||
            signal.sessionId != activeSessionId)
        {
            return;
        }

        if (peerConnection == null || !remoteDescriptionSet)
        {
            pendingRemoteCandidates.Enqueue(signal);
            return;
        }

        AddRemoteCandidate(signal);
    }

    private void FlushPendingRemoteCandidates()
    {
        while (pendingRemoteCandidates.Count > 0)
            AddRemoteCandidate(pendingRemoteCandidates.Dequeue());
    }

    private void AddRemoteCandidate(IceSignal signal)
    {
        if (peerConnection == null || signal.sessionId != activeSessionId)
            return;

        RTCIceCandidateInit initialization = new RTCIceCandidateInit
        {
            candidate = signal.candidate,
            sdpMid = signal.sdpMid,
            sdpMLineIndex = signal.sdpMLineIndex >= 0 ? signal.sdpMLineIndex : null
        };

        peerConnection.AddIceCandidate(new RTCIceCandidate(initialization));
        LogCandidateType("WebRtcVideoReceiver remote", signal.candidate);
    }

    private bool MatchesActiveSession(SessionSignal signal)
    {
        return signal != null &&
               !string.IsNullOrEmpty(signal.sessionId) &&
               signal.sessionId == activeSessionId;
    }

    private void HandleRemoteStop(PlayerRef from, SessionSignal signal)
    {
        SendJson(
            from,
            WebRtcWebcamSender.StopAckType,
            JsonUtility.ToJson(new SessionSignal { sessionId = signal.sessionId })
        );

        CleanupLocalSession("Audience stopped the video session.");
    }

    public void RequestStopReceiving()
    {
        if (string.IsNullOrEmpty(activeSessionId) || remotePlayer == PlayerRef.None)
        {
            CleanupLocalSession("Video receiver stopped locally.");
            return;
        }

        SetState(SessionState.Stopping, "Waiting for the Audience to acknowledge video stop.");

        SendJson(
            remotePlayer,
            WebRtcWebcamSender.StopType,
            JsonUtility.ToJson(new SessionSignal { sessionId = activeSessionId })
        );

        StartStopAckTimeout(activeSessionId);
    }

    public void StopReceiving()
    {
        CleanupLocalSession("Video receiver stopped locally.");
    }

    private void StartConnectionTimeout(string sessionId)
    {
        CancelConnectionTimeout();
        connectionTimeoutCoroutine = StartCoroutine(ConnectionTimeoutRoutine(sessionId));
    }

    private IEnumerator ConnectionTimeoutRoutine(string sessionId)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(1f, connectionTimeoutSeconds));
        connectionTimeoutCoroutine = null;

        if (sessionId == activeSessionId && State != SessionState.Connected)
            Fail("ConnectionTimeout", "Video did not become ready before the connection timeout.", true);
    }

    private void CancelConnectionTimeout()
    {
        if (connectionTimeoutCoroutine == null)
            return;

        StopCoroutine(connectionTimeoutCoroutine);
        connectionTimeoutCoroutine = null;
    }

    private void StartDisconnectedGracePeriod(string sessionId)
    {
        CancelDisconnectedGracePeriod();
        disconnectedCoroutine = StartCoroutine(DisconnectedGraceRoutine(sessionId));
    }

    private IEnumerator DisconnectedGraceRoutine(string sessionId)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, disconnectedGraceSeconds));
        disconnectedCoroutine = null;

        if (sessionId == activeSessionId && !iceConnected)
            Fail("ConnectionLost", "Video connection did not recover after disconnection.", true);
    }

    private void CancelDisconnectedGracePeriod()
    {
        if (disconnectedCoroutine == null)
            return;

        StopCoroutine(disconnectedCoroutine);
        disconnectedCoroutine = null;
    }

    private void StartStopAckTimeout(string sessionId)
    {
        CancelStopAckTimeout();
        stopAckCoroutine = StartCoroutine(StopAckTimeoutRoutine(sessionId));
    }

    private IEnumerator StopAckTimeoutRoutine(string sessionId)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, stopAckTimeoutSeconds));
        stopAckCoroutine = null;

        if (sessionId == activeSessionId && State == SessionState.Stopping)
            CleanupLocalSession("Video stop acknowledgement timed out; local resources were released.");
    }

    private void CancelStopAckTimeout()
    {
        if (stopAckCoroutine == null)
            return;

        StopCoroutine(stopAckCoroutine);
        stopAckCoroutine = null;
    }

    private void Fail(string code, string message, bool notifyRemote)
    {
        Debug.LogError("WebRtcVideoReceiver: " + code + ": " + message);

        string failedSessionId = activeSessionId;
        PlayerRef failedRemote = remotePlayer;

        if (notifyRemote && !string.IsNullOrEmpty(failedSessionId) && failedRemote != PlayerRef.None)
        {
            SendJson(
                failedRemote,
                WebRtcWebcamSender.ErrorType,
                JsonUtility.ToJson(new SessionSignal
                {
                    sessionId = failedSessionId,
                    errorCode = code,
                    message = message
                })
            );
        }

        operationCoroutine = null;
        CancelConnectionTimeout();
        CancelDisconnectedGracePeriod();
        CancelStopAckTimeout();
        ReleaseMediaResources();
        activeSessionId = "";
        remotePlayer = PlayerRef.None;
        SetState(SessionState.Failed, code + ": " + message);
    }

    private void CleanupLocalSession(string message, bool setIdle = true)
    {
        if (State != SessionState.Idle && State != SessionState.WaitingForSignalHub)
            SetState(SessionState.Stopping, message);

        if (operationCoroutine != null)
        {
            StopCoroutine(operationCoroutine);
            operationCoroutine = null;
        }

        CancelConnectionTimeout();
        CancelDisconnectedGracePeriod();
        CancelStopAckTimeout();
        ReleaseMediaResources();
        activeSessionId = "";
        remotePlayer = PlayerRef.None;

        if (setIdle)
            SetState(SessionState.Idle, message);
    }

    private void ReleaseMediaResources()
    {
        if (isCleaningUp)
            return;

        isCleaningUp = true;
        remoteDescriptionSet = false;
        iceConnected = false;
        firstFrameReceived = false;
        connectedSignalSent = false;
        pendingRemoteCandidates.Clear();

        for (int i = 0; i < displayScreens.Count; i++)
        {
            if (displayScreens[i] != null)
                displayScreens[i].ClearTexture();
        }

        foreach (RemoteVideoStream stream in remoteStreams.Values)
        {
            if (stream.track == null)
                continue;

            if (stream.videoHandler != null)
                stream.track.OnVideoReceived -= stream.videoHandler;

            stream.track.Dispose();
        }

        remoteStreams.Clear();
        availableStreams.Clear();
        AvailableStreamsChanged?.Invoke();

        if (peerConnection != null)
        {
            peerConnection.Close();
            peerConnection.Dispose();
            peerConnection = null;
        }

        isCleaningUp = false;
    }

    private RTCConfiguration BuildRtcConfiguration()
    {
        List<RTCIceServer> iceServers = new List<RTCIceServer>();

        if (useGoogleStun)
        {
            iceServers.Add(new RTCIceServer
            {
                urls = new[] { "stun:stun.relay.metered.ca:80" }
            });
        }

        if (useTurn)
        {
            List<string> urls = new List<string>();
            if (!string.IsNullOrWhiteSpace(turnUrlUdp))
                urls.Add(turnUrlUdp);
            if (!string.IsNullOrWhiteSpace(turnUrlTcp))
                urls.Add(turnUrlTcp);

            if (urls.Count > 0)
            {
                iceServers.Add(new RTCIceServer
                {
                    urls = urls.ToArray(),
                    username = turnUsername,
                    credential = turnCredential
                });
            }
        }

        return new RTCConfiguration
        {
            iceServers = iceServers.ToArray()
        };
    }

    private static bool SendJson(PlayerRef target, string type, string json)
    {
        if (WebRtcSignalHub.Instance == null || target == PlayerRef.None)
            return false;

        WebRtcSignalHub.Instance.SendSignal(target, type, json);
        return true;
    }

    private static void LogCandidateType(string prefix, string candidate)
    {
        if (string.IsNullOrEmpty(candidate))
            return;

        if (candidate.Contains(" typ relay "))
            Debug.Log(prefix + ": Candidate type = relay / TURN");
        else if (candidate.Contains(" typ srflx "))
            Debug.Log(prefix + ": Candidate type = srflx / STUN");
        else if (candidate.Contains(" typ host "))
            Debug.Log(prefix + ": Candidate type = host / local");
        else
            Debug.Log(prefix + ": Candidate type = unknown");
    }

    private void SetState(SessionState state, string message)
    {
        State = state;
        Debug.Log("WebRtcVideoReceiver: State = " + state + ". " + message);
        StateChanged?.Invoke(state, message);
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            StopReceiving();
    }

    private void OnDisable()
    {
        VideoDisplayScreen.RegistryChanged -= OnDisplayScreenRegistryChanged;

        if (signalHubCoroutine != null)
        {
            StopCoroutine(signalHubCoroutine);
            signalHubCoroutine = null;
        }

        if (signalHubSubscribed && WebRtcSignalHub.Instance != null)
            WebRtcSignalHub.Instance.OnSignalReceived -= OnSignalReceived;

        signalHubSubscribed = false;
        StopReceiving();
    }

    private void OnDestroy()
    {
        VideoDisplayScreen.RegistryChanged -= OnDisplayScreenRegistryChanged;

        if (signalHubSubscribed && WebRtcSignalHub.Instance != null)
            WebRtcSignalHub.Instance.OnSignalReceived -= OnSignalReceived;

        signalHubSubscribed = false;
        StopReceiving();
    }
}
