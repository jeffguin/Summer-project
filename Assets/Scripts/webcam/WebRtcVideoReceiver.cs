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
    private VideoStreamTrack remoteVideoTrack;
    private Texture currentRemoteTexture;
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
    private float lastRemoteFrameTime;
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
        ResolveVideoDisplayScreen();
    }

    private void OnEnable()
    {
        if (signalHubCoroutine == null)
            signalHubCoroutine = StartCoroutine(WaitForSignalHub());
    }

    private void Update()
    {
        if (State == SessionState.Connected &&
            lastRemoteFrameTime > 0f &&
            Time.realtimeSinceStartup - lastRemoteFrameTime > Mathf.Max(1f, remoteFrameTimeoutSeconds))
        {
            Fail("RemoteFrameTimeout", "The remote camera stopped delivering video frames.", true);
        }
    }

    private void ResolveVideoDisplayScreen()
    {
        if (videoDisplayScreen != null)
            return;

        videoDisplayScreen =
            FindFirstObjectByType<VideoDisplayScreen>(FindObjectsInactive.Include);

        if (videoDisplayScreen == null)
            Debug.LogError("WebRtcVideoReceiver: VideoDisplayScreen is missing.");
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
                AttachRemoteVideoTrack(videoTrack);
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

    private void AttachRemoteVideoTrack(VideoStreamTrack videoTrack)
    {
        if (videoTrack == null)
            return;

        if (remoteVideoTrack != null && remoteVideoTrack != videoTrack)
        {
            remoteVideoTrack.OnVideoReceived -= OnRemoteVideoReceived;
            remoteVideoTrack.Dispose();
        }

        remoteVideoTrack = videoTrack;
        remoteVideoTrack.OnVideoReceived -= OnRemoteVideoReceived;
        remoteVideoTrack.OnVideoReceived += OnRemoteVideoReceived;
        SetState(SessionState.Connecting, "Remote video track received; waiting for the first frame.");
    }

    private void OnRemoteVideoReceived(Texture texture)
    {
        if (texture == null || texture.width <= 0 || texture.height <= 0)
            return;

        lastRemoteFrameTime = Time.realtimeSinceStartup;

        ResolveVideoDisplayScreen();
        if (videoDisplayScreen == null)
        {
            Fail("DisplayMissing", "VideoDisplayScreen is unavailable.", true);
            return;
        }

        if (currentRemoteTexture != texture)
        {
            currentRemoteTexture = texture;
            videoDisplayScreen.SetTexture(texture);
        }

        if (!firstFrameReceived)
        {
            firstFrameReceived = true;
            Debug.Log(
                "WebRtcVideoReceiver: First remote frame received. Size = " +
                texture.width + "x" + texture.height
            );
        }

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
        currentRemoteTexture = null;
        lastRemoteFrameTime = 0f;
        pendingRemoteCandidates.Clear();

        if (videoDisplayScreen != null)
            videoDisplayScreen.ClearTexture();

        if (remoteVideoTrack != null)
        {
            remoteVideoTrack.OnVideoReceived -= OnRemoteVideoReceived;
            remoteVideoTrack.Dispose();
            remoteVideoTrack = null;
        }

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
        if (signalHubSubscribed && WebRtcSignalHub.Instance != null)
            WebRtcSignalHub.Instance.OnSignalReceived -= OnSignalReceived;

        signalHubSubscribed = false;
        StopReceiving();
    }
}
