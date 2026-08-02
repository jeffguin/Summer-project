using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class WebRtcWebcamSender : MonoBehaviour
{
    public enum SessionState
    {
        Idle,
        WaitingForSignalHub,
        PreparingDevice,
        LocalTrackReady,
        Negotiating,
        Connecting,
        Connected,
        Recovering,
        Stopping,
        Failed
    }

    public const string OfferType = "video.offer";
    public const string AnswerType = "video.answer";
    public const string CandidateType = "video.ice";
    public const string ConnectedType = "video.session.connected";
    public const string StopType = "video.session.stop";
    public const string StopAckType = "video.session.stop.ack";
    public const string ErrorType = "video.error";

    [Header("References")]
    [SerializeField] private LocalWebcamManager localWebcamManager;

    [Header("Optional Local Test UI")]
    [SerializeField] private Button startStreamButton;

    [Header("ICE / STUN / TURN Settings")]
    [SerializeField] private bool useGoogleStun = true;
    [SerializeField] private bool useTurn = false;
    [SerializeField] private string turnUrlUdp = "turn:YOUR_TURN_SERVER:3478?transport=udp";
    [SerializeField] private string turnUrlTcp = "turn:YOUR_TURN_SERVER:3478?transport=tcp";
    [SerializeField] private string turnUsername = "YOUR_USERNAME";
    [SerializeField] private string turnCredential = "YOUR_PASSWORD";

    [Header("Timeouts")]
    [SerializeField] private float cameraReadyTimeoutSeconds = 5f;
    [SerializeField] private float cameraStallTimeoutSeconds = 5f;
    [SerializeField] private float connectionTimeoutSeconds = 20f;
    [SerializeField] private float disconnectedGraceSeconds = 3f;
    [SerializeField] private float stopAckTimeoutSeconds = 2f;

    private RTCPeerConnection peerConnection;
    private VideoStreamTrack videoTrack;
    private RTCRtpSender localVideoSender;
    private Coroutine operationCoroutine;
    private Coroutine signalHubCoroutine;
    private Coroutine connectionTimeoutCoroutine;
    private Coroutine disconnectedCoroutine;
    private Coroutine stopAckCoroutine;

    private bool signalHubSubscribed;
    private bool remoteDescriptionSet;
    private bool iceConnected;
    private bool remoteFrameConfirmed;
    private bool isCleaningUp;
    private float lastCameraFrameTime;
    private string activeSessionId = "";
    private PlayerRef remotePlayer = PlayerRef.None;

    private readonly Queue<IceSignal> pendingRemoteCandidates = new Queue<IceSignal>();

    public event Action<SessionState, string> StateChanged;

    public SessionState State { get; private set; } = SessionState.Idle;
    public string ActiveSessionId => activeSessionId;
    public PlayerRef RemotePlayer => remotePlayer;
    public bool IsConnected => State == SessionState.Connected;

    [Serializable]
    private sealed class SessionSignal
    {
        public string sessionId;
        public string sdp;
        public string errorCode;
        public string message;
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

        if (startStreamButton != null)
            startStreamButton.onClick.AddListener(StartWebcamStream);
    }

    private void OnEnable()
    {
        if (signalHubCoroutine == null)
            signalHubCoroutine = StartCoroutine(WaitForSignalHub());
    }

    private void Update()
    {
        if (string.IsNullOrEmpty(activeSessionId) || localWebcamManager == null)
            return;

        WebCamTexture webcamTexture = localWebcamManager.GetCurrentWebcamTexture();
        if (webcamTexture != null && webcamTexture.didUpdateThisFrame)
            lastCameraFrameTime = Time.realtimeSinceStartup;

        bool shouldMonitorFrames =
            State == SessionState.LocalTrackReady ||
            State == SessionState.Negotiating ||
            State == SessionState.Connecting ||
            State == SessionState.Connected ||
            State == SessionState.Recovering;

        if (shouldMonitorFrames &&
            lastCameraFrameTime > 0f &&
            Time.realtimeSinceStartup - lastCameraFrameTime > Mathf.Max(1f, cameraStallTimeoutSeconds))
        {
            Fail("CameraFrameStalled", "The selected camera stopped producing frames.", true);
        }
    }

    private IEnumerator WaitForSignalHub()
    {
        SetState(SessionState.WaitingForSignalHub, "Waiting for the Fusion WebRTC signal hub.");

        while (WebRtcSignalHub.Instance == null)
            yield return null;

        SubscribeToSignalHub();
        signalHubCoroutine = null;

        if (State == SessionState.WaitingForSignalHub)
            SetState(SessionState.Idle, "Video sender is ready.");
    }

    private void SubscribeToSignalHub()
    {
        if (WebRtcSignalHub.Instance == null)
            return;

        WebRtcSignalHub.Instance.OnSignalReceived -= OnSignalReceived;
        WebRtcSignalHub.Instance.OnSignalReceived += OnSignalReceived;
        signalHubSubscribed = true;
    }

    // Kept for the optional local test button. The production flow should call
    // the overload that receives an Actor-created session id and explicit target.
    public void StartWebcamStream()
    {
        if (WebRtcSignalHub.Instance == null)
        {
            Debug.LogWarning("WebRtcWebcamSender: SignalHub is not ready.");
            return;
        }

        PlayerRef target = WebRtcSignalHub.Instance.GetOtherPlayer();
        if (target == PlayerRef.None)
        {
            Debug.LogWarning("WebRtcWebcamSender: No receiver player is available.");
            return;
        }

        WebCamTexture currentTexture =
            localWebcamManager != null ? localWebcamManager.GetCurrentWebcamTexture() : null;

        if (currentTexture == null || !currentTexture.isPlaying)
        {
            Debug.LogWarning("WebRtcWebcamSender: Start the local webcam before using the test button.");
            return;
        }

        BeginSession(Guid.NewGuid().ToString("N"), target, -1, false);
    }

    public void StartWebcamStream(string sessionId, PlayerRef target, int cameraIndex)
    {
        BeginSession(sessionId, target, cameraIndex, true);
    }

    private void BeginSession(string sessionId, PlayerRef target, int cameraIndex, bool startCamera)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            Debug.LogWarning("WebRtcWebcamSender: Cannot start without a session id.");
            return;
        }

        if (target == PlayerRef.None)
        {
            Debug.LogWarning("WebRtcWebcamSender: Cannot start without an explicit receiver.");
            return;
        }

        if (WebRtcSignalHub.Instance == null)
        {
            Debug.LogWarning("WebRtcWebcamSender: SignalHub is not ready.");
            return;
        }

        if (!signalHubSubscribed)
            SubscribeToSignalHub();

        CleanupLocalSession("Preparing a new video session.", false);
        activeSessionId = sessionId;
        remotePlayer = target;
        operationCoroutine = StartCoroutine(StartSenderRoutine(sessionId, cameraIndex, startCamera));
    }

    private IEnumerator StartSenderRoutine(string sessionId, int cameraIndex, bool startCamera)
    {
        SetState(SessionState.PreparingDevice, "Starting the selected camera.");

        if (localWebcamManager == null)
        {
            operationCoroutine = null;
            Fail("CameraManagerMissing", "LocalWebcamManager is not assigned.", true);
            yield break;
        }

        if (startCamera && !localWebcamManager.TryStartCameraByIndex(cameraIndex, out string cameraError))
        {
            operationCoroutine = null;
            Fail("CameraStartFailed", cameraError, true);
            yield break;
        }

        WebCamTexture webcamTexture = localWebcamManager.GetCurrentWebcamTexture();
        if (webcamTexture == null || !webcamTexture.isPlaying)
        {
            operationCoroutine = null;
            Fail("CameraNotPlaying", "The selected camera did not start.", true);
            yield break;
        }

        float cameraDeadline = Time.realtimeSinceStartup + Mathf.Max(1f, cameraReadyTimeoutSeconds);
        while (sessionId == activeSessionId &&
               !localWebcamManager.IsCurrentCameraReady &&
               Time.realtimeSinceStartup < cameraDeadline)
        {
            yield return null;
        }

        if (sessionId != activeSessionId)
        {
            operationCoroutine = null;
            yield break;
        }

        if (!localWebcamManager.IsCurrentCameraReady)
        {
            operationCoroutine = null;
            Fail(
                "CameraReadyTimeout",
                "The camera did not provide a valid frame before the startup timeout.",
                true
            );
            yield break;
        }

        lastCameraFrameTime = Time.realtimeSinceStartup;

        if (!CreatePeerConnection())
        {
            operationCoroutine = null;
            Fail("PeerConnectionCreationFailed", "Could not create the video PeerConnection.", true);
            yield break;
        }

        try
        {
            videoTrack = new VideoStreamTrack(webcamTexture);
            localVideoSender = peerConnection.AddTrack(videoTrack);
        }
        catch (Exception exception)
        {
            operationCoroutine = null;
            Fail("TrackCreationFailed", exception.Message, true);
            yield break;
        }

        if (localVideoSender == null)
        {
            operationCoroutine = null;
            Fail("TrackAddFailed", "The video track could not be added to the PeerConnection.", true);
            yield break;
        }

        SetState(
            SessionState.LocalTrackReady,
            "Camera track is ready: " + localWebcamManager.CurrentDeviceName + "."
        );

        RTCSessionDescriptionAsyncOperation offerOperation = peerConnection.CreateOffer();
        yield return offerOperation;

        if (sessionId != activeSessionId)
        {
            operationCoroutine = null;
            yield break;
        }

        if (offerOperation.IsError)
        {
            operationCoroutine = null;
            Fail("CreateOfferFailed", offerOperation.Error.message, true);
            yield break;
        }

        RTCSessionDescription offer = offerOperation.Desc;
        RTCSetSessionDescriptionAsyncOperation localOperation =
            peerConnection.SetLocalDescription(ref offer);
        yield return localOperation;

        if (sessionId != activeSessionId)
        {
            operationCoroutine = null;
            yield break;
        }

        if (localOperation.IsError)
        {
            operationCoroutine = null;
            Fail("SetLocalOfferFailed", localOperation.Error.message, true);
            yield break;
        }

        SessionSignal signal = new SessionSignal
        {
            sessionId = activeSessionId,
            sdp = offer.sdp
        };

        if (!SendJson(remotePlayer, OfferType, JsonUtility.ToJson(signal)))
        {
            operationCoroutine = null;
            Fail("SignalHubUnavailable", "The video offer could not be sent.", false);
            yield break;
        }

        operationCoroutine = null;
        SetState(SessionState.Negotiating, "Video offer sent; waiting for the Actor answer.");
        StartConnectionTimeout(sessionId);
    }

    private bool CreatePeerConnection()
    {
        try
        {
            RTCConfiguration configuration = BuildRtcConfiguration();
            peerConnection = new RTCPeerConnection(ref configuration);
            ConfigurePeerConnectionCallbacks();
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError("WebRtcWebcamSender: PeerConnection creation failed: " + exception);
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

            LogCandidateType("WebRtcWebcamSender", candidate.Candidate);

            IceSignal signal = new IceSignal
            {
                sessionId = activeSessionId,
                candidate = candidate.Candidate,
                sdpMid = candidate.SdpMid,
                sdpMLineIndex = candidate.SdpMLineIndex.HasValue
                    ? candidate.SdpMLineIndex.Value
                    : -1
            };

            SendJson(remotePlayer, CandidateType, JsonUtility.ToJson(signal));
        };

        peerConnection.OnTrack = trackEvent =>
        {
            Debug.LogWarning(
                "WebRtcWebcamSender: The send-only video connection received an unexpected " +
                trackEvent.Track.Kind + " track."
            );
        };

        peerConnection.OnIceConnectionChange = state =>
        {
            if (isCleaningUp)
                return;

            Debug.Log("WebRtcWebcamSender: ICE state = " + state + ", Session = " + activeSessionId);

            if (state == RTCIceConnectionState.Connected || state == RTCIceConnectionState.Completed)
            {
                iceConnected = true;
                CancelDisconnectedGracePeriod();
                UpdateConnectedState();
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
                "WebRtcWebcamSender: PeerConnection state = " + state +
                ", Session = " + activeSessionId
            );

            if (state == RTCPeerConnectionState.Failed)
                Fail("PeerConnectionFailed", "Video PeerConnection entered the Failed state.", true);
        };
    }

    private void OnSignalReceived(PlayerRef from, string type, string payload)
    {
        if (from != remotePlayer || string.IsNullOrEmpty(activeSessionId))
            return;

        if (type == AnswerType)
        {
            SessionSignal signal = JsonUtility.FromJson<SessionSignal>(payload);
            if (MatchesActiveSession(signal))
                StartCoroutine(HandleAnswerRoutine(signal));
        }
        else if (type == CandidateType)
        {
            HandleRemoteCandidate(JsonUtility.FromJson<IceSignal>(payload));
        }
        else if (type == ConnectedType)
        {
            SessionSignal signal = JsonUtility.FromJson<SessionSignal>(payload);
            if (MatchesActiveSession(signal))
            {
                remoteFrameConfirmed = true;
                UpdateConnectedState();
            }
        }
        else if (type == StopType)
        {
            SessionSignal signal = JsonUtility.FromJson<SessionSignal>(payload);
            if (MatchesActiveSession(signal))
                HandleRemoteStop(from, signal);
        }
        else if (type == StopAckType)
        {
            SessionSignal signal = JsonUtility.FromJson<SessionSignal>(payload);
            if (State == SessionState.Stopping && MatchesActiveSession(signal))
                CleanupLocalSession("Remote endpoint acknowledged the video stop.");
        }
        else if (type == ErrorType)
        {
            SessionSignal signal = JsonUtility.FromJson<SessionSignal>(payload);
            if (MatchesActiveSession(signal))
                Fail(signal.errorCode ?? "RemoteVideoError", signal.message ?? "Remote video failed.", false);
        }
    }

    private IEnumerator HandleAnswerRoutine(SessionSignal signal)
    {
        if (!MatchesActiveSession(signal) || peerConnection == null || string.IsNullOrEmpty(signal.sdp))
            yield break;

        RTCSessionDescription answer = new RTCSessionDescription
        {
            type = RTCSdpType.Answer,
            sdp = signal.sdp
        };

        RTCSetSessionDescriptionAsyncOperation remoteOperation =
            peerConnection.SetRemoteDescription(ref answer);
        yield return remoteOperation;

        if (!MatchesActiveSession(signal))
            yield break;

        if (remoteOperation.IsError)
        {
            Fail("SetRemoteAnswerFailed", remoteOperation.Error.message, true);
            yield break;
        }

        remoteDescriptionSet = true;
        FlushPendingRemoteCandidates();
        SetState(SessionState.Connecting, "Actor answer applied; connecting video ICE.");
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
        LogCandidateType("WebRtcWebcamSender remote", signal.candidate);
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
            StopAckType,
            JsonUtility.ToJson(new SessionSignal { sessionId = signal.sessionId })
        );

        CleanupLocalSession("The Actor stopped the video session.");
    }

    public void StopWebcamStream()
    {
        RequestStopWebcamStream();
    }

    public void RequestStopWebcamStream()
    {
        if (string.IsNullOrEmpty(activeSessionId) || remotePlayer == PlayerRef.None)
        {
            CleanupLocalSession("Video sender stopped locally.");
            return;
        }

        SetState(SessionState.Stopping, "Waiting for the remote endpoint to acknowledge video stop.");

        SendJson(
            remotePlayer,
            StopType,
            JsonUtility.ToJson(new SessionSignal { sessionId = activeSessionId })
        );

        StartStopAckTimeout(activeSessionId);
    }

    public void ForceStopWebcamStream()
    {
        CleanupLocalSession("Video sender stopped locally.");
    }

    private void UpdateConnectedState()
    {
        if (!iceConnected || !remoteFrameConfirmed || string.IsNullOrEmpty(activeSessionId))
            return;

        CancelConnectionTimeout();
        SetState(SessionState.Connected, "Actor confirmed receipt of the first video frame.");
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
        if (stopAckCoroutine != null)
            StopCoroutine(stopAckCoroutine);

        stopAckCoroutine = StartCoroutine(StopAckTimeoutRoutine(sessionId));
    }

    private IEnumerator StopAckTimeoutRoutine(string sessionId)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, stopAckTimeoutSeconds));
        stopAckCoroutine = null;

        if (sessionId == activeSessionId && State == SessionState.Stopping)
            CleanupLocalSession("Video stop acknowledgement timed out; local resources were released.");
    }

    private void Fail(string code, string message, bool notifyRemote)
    {
        Debug.LogError("WebRtcWebcamSender: " + code + ": " + message);

        string failedSessionId = activeSessionId;
        PlayerRef failedRemote = remotePlayer;

        if (notifyRemote && !string.IsNullOrEmpty(failedSessionId) && failedRemote != PlayerRef.None)
        {
            SendJson(
                failedRemote,
                ErrorType,
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

    private void CancelStopAckTimeout()
    {
        if (stopAckCoroutine == null)
            return;

        StopCoroutine(stopAckCoroutine);
        stopAckCoroutine = null;
    }

    private void ReleaseMediaResources()
    {
        if (isCleaningUp)
            return;

        isCleaningUp = true;
        remoteDescriptionSet = false;
        iceConnected = false;
        remoteFrameConfirmed = false;
        lastCameraFrameTime = 0f;
        pendingRemoteCandidates.Clear();

        if (peerConnection != null && localVideoSender != null)
        {
            peerConnection.RemoveTrack(localVideoSender);
            localVideoSender = null;
        }

        if (videoTrack != null)
        {
            videoTrack.Dispose();
            videoTrack = null;
        }

        if (peerConnection != null)
        {
            peerConnection.Close();
            peerConnection.Dispose();
            peerConnection = null;
        }

        if (localWebcamManager != null)
            localWebcamManager.StopWebcam();

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
        Debug.Log("WebRtcWebcamSender: State = " + state + ". " + message);
        StateChanged?.Invoke(state, message);
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            ForceStopWebcamStream();
    }

    private void OnDisable()
    {
        if (signalHubCoroutine != null)
        {
            StopCoroutine(signalHubCoroutine);
            signalHubCoroutine = null;
        }

        if (signalHubSubscribed && WebRtcSignalHub.Instance != null)
            WebRtcSignalHub.Instance.OnSignalReceived -= OnSignalReceived;

        signalHubSubscribed = false;
        ForceStopWebcamStream();
    }

    private void OnDestroy()
    {
        if (startStreamButton != null)
            startStreamButton.onClick.RemoveListener(StartWebcamStream);

        if (signalHubSubscribed && WebRtcSignalHub.Instance != null)
            WebRtcSignalHub.Instance.OnSignalReceived -= OnSignalReceived;

        signalHubSubscribed = false;
        ForceStopWebcamStream();
    }
}
