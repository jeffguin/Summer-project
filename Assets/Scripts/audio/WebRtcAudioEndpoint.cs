using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using Unity.WebRTC;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WebRtcAudioEndpoint : MonoBehaviour
{
    public enum EndpointRole
    {
        Actor = 0,
        Audience = 1
    }

    public enum SessionState
    {
        Idle,
        WaitingForSignalHub,
        CaptureStarting,
        LocalTrackReady,
        Negotiating,
        Connecting,
        Connected,
        Stopping,
        Failed
    }

    public const string DeviceListRequestType = "audio.device.list.request";
    public const string DeviceListType = "audio.device.list";
    public const string DeviceSelectType = "audio.device.select";
    public const string DeviceSelectAckType = "audio.device.select.ack";
    public const string StartRequestType = "audio.session.start";
    public const string StopType = "audio.session.stop";
    public const string OfferType = "audio.offer";
    public const string AnswerType = "audio.answer";
    public const string CandidateType = "audio.ice";
    public const string ErrorType = "audio.error";
    public const string StatusType = "audio.status";

    [Header("Endpoint")]
    [SerializeField] private EndpointRole role = EndpointRole.Actor;
    [SerializeField] private MicrophoneCaptureService captureService;
    [SerializeField] private AudioSource remotePlaybackAudioSource;

    [Header("ICE / STUN / TURN")]
    [SerializeField] private bool useStun = true;
    [SerializeField] private string stunUrl = "stun:stun.relay.metered.ca:80";
    [SerializeField] private bool useTurn = false;
    [SerializeField] private string turnUrlUdp = "";
    [SerializeField] private string turnUrlTcp = "";
    [SerializeField] private string turnUsername = "";
    [SerializeField] private string turnCredential = "";

    [Header("Timeouts")]
    [SerializeField] private float connectionTimeoutSeconds = 20f;

    private RTCPeerConnection audioPeerConnection;
    private RTCRtpSender localAudioSender;
    private AudioStreamTrack remoteAudioTrack;
    private Coroutine operationCoroutine;
    private Coroutine signalHubCoroutine;
    private Coroutine connectionTimeoutCoroutine;
    private bool signalHubSubscribed;
    private bool remoteDescriptionSet;
    private bool iceConnected;
    private string activeSessionId = "";

    private readonly Queue<IceSignal> pendingRemoteCandidates = new Queue<IceSignal>();

    public event Action<string[], string> AudienceMicrophoneListReceived;
    public event Action<string, bool, string> AudienceMicrophoneSelectionAcknowledged;
    public event Action<SessionState, string> StateChanged;

    public EndpointRole Role => role;
    public SessionState State { get; private set; } = SessionState.Idle;
    public string ActiveSessionId => activeSessionId;
    public bool IsConnected => State == SessionState.Connected;

    [Serializable]
    private class SessionSignal
    {
        public string sessionId;
        public string sdp;
        public string errorCode;
        public string message;
    }

    [Serializable]
    private class IceSignal
    {
        public string sessionId;
        public string candidate;
        public string sdpMid;
        public int sdpMLineIndex;
    }

    [Serializable]
    private class DeviceListSignal
    {
        public string[] devices;
        public string selectedDevice;
        public string endpointLabel;
    }

    [Serializable]
    private class DeviceSelectSignal
    {
        public string deviceName;
        public bool success;
        public string message;
    }

    private void Awake()
    {
        WebRtcRuntimePump.EnsureExists();
        EnsureAudioObjects();
    }

    private void OnEnable()
    {
        if (signalHubCoroutine == null)
            signalHubCoroutine = StartCoroutine(WaitForSignalHub());
    }

    private IEnumerator WaitForSignalHub()
    {
        SetState(SessionState.WaitingForSignalHub, "Waiting for the Fusion WebRTC signal hub.");

        while (WebRtcSignalHub.Instance == null)
            yield return null;

        SubscribeToSignalHub();

        if (role == EndpointRole.Audience)
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while (WebRtcSignalHub.Instance.GetOtherPlayer() == PlayerRef.None &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForSeconds(0.5f);
            }

            PlayerRef actor = WebRtcSignalHub.Instance.GetOtherPlayer();
            if (actor != PlayerRef.None)
                SendAudienceDeviceList(actor);
        }

        signalHubCoroutine = null;

        if (State == SessionState.WaitingForSignalHub)
            SetState(SessionState.Idle, "Audio endpoint is ready.");
    }

    private void SubscribeToSignalHub()
    {
        if (WebRtcSignalHub.Instance == null)
            return;

        WebRtcSignalHub.Instance.OnSignalReceived -= OnSignalReceived;
        WebRtcSignalHub.Instance.OnSignalReceived += OnSignalReceived;
        signalHubSubscribed = true;
    }

    public string[] GetLocalMicrophoneDevices()
    {
        EnsureAudioObjects();
        return captureService.GetDevices();
    }

    public string GetMicrophoneDeviceName()
    {
        EnsureAudioObjects();
        return captureService.SelectedDeviceName;
    }

    public bool SetMicrophoneDeviceName(string deviceName)
    {
        EnsureAudioObjects();

        string error;
        bool applied = captureService.SetSelectedDevice(deviceName, out error);

        if (!applied)
            Debug.LogWarning(Prefix + error);

        if (applied && State != SessionState.Idle && State != SessionState.Failed)
        {
            Debug.LogWarning(Prefix + "The selected microphone will be used after restarting the audio session.");
        }

        return applied;
    }

    public bool RequestAudienceMicrophoneList()
    {
        if (role != EndpointRole.Actor)
            return false;

        return SendJsonToOther(DeviceListRequestType, "{}");
    }

    public bool SelectAudienceMicrophone(string deviceName)
    {
        if (role != EndpointRole.Actor)
            return false;

        DeviceSelectSignal signal = new DeviceSelectSignal
        {
            deviceName = deviceName ?? ""
        };

        return SendJsonToOther(DeviceSelectType, JsonUtility.ToJson(signal));
    }

    public void StartAudioSession()
    {
        if (role != EndpointRole.Actor)
        {
            Debug.LogWarning(Prefix + "Only the Actor endpoint can initiate an audio session.");
            return;
        }

        if (operationCoroutine != null)
            StopCoroutine(operationCoroutine);

        CleanupLocalSession("Preparing a new Actor audio session.", false);
        operationCoroutine = StartCoroutine(StartActorSessionRoutine());
    }

    public void StopAudioSession()
    {
        string sessionToStop = activeSessionId;

        if (!string.IsNullOrEmpty(sessionToStop))
        {
            SendJsonToOther(
                StopType,
                JsonUtility.ToJson(new SessionSignal { sessionId = sessionToStop })
            );
        }

        CleanupLocalSession("Audio session stopped locally.");
    }

    private IEnumerator StartActorSessionRoutine()
    {
        activeSessionId = Guid.NewGuid().ToString("N");

        SetState(SessionState.CaptureStarting, "Requesting permission and starting the Actor microphone.");

        MicrophoneCaptureService.CaptureResult captureResult = null;
        yield return captureService.StartCapture(result => captureResult = result);

        if (captureResult == null || !captureResult.Success)
        {
            string code = captureResult != null ? captureResult.ErrorCode : "CaptureUnknownFailure";
            string message = captureResult != null ? captureResult.Message : "Microphone capture did not return a result.";
            Fail(code, message, false);
            operationCoroutine = null;
            yield break;
        }

        SetState(SessionState.LocalTrackReady, "Actor microphone track is ready.");

        if (!CreatePeerConnectionAndAddLocalTrack())
        {
            Fail("PeerConnectionCreationFailed", "Could not create the Actor audio PeerConnection.", false);
            operationCoroutine = null;
            yield break;
        }

        SessionSignal startSignal = new SessionSignal
        {
            sessionId = activeSessionId
        };

        if (!SendJsonToOther(StartRequestType, JsonUtility.ToJson(startSignal)))
        {
            Fail("AudienceUnavailable", "No Audience player is available for audio.", false);
            operationCoroutine = null;
            yield break;
        }

        SetState(SessionState.Negotiating, "Actor is ready; waiting for the Audience audio offer.");
        StartConnectionTimeout(activeSessionId);
        operationCoroutine = null;
    }

    private IEnumerator StartAudienceOfferRoutine(string requestedSessionId)
    {
        activeSessionId = requestedSessionId;

        SetState(SessionState.CaptureStarting, "Starting the selected Audience microphone.");

        MicrophoneCaptureService.CaptureResult captureResult = null;
        yield return captureService.StartCapture(result => captureResult = result);

        if (captureResult == null || !captureResult.Success)
        {
            string code = captureResult != null ? captureResult.ErrorCode : "CaptureUnknownFailure";
            string message = captureResult != null ? captureResult.Message : "Microphone capture did not return a result.";
            Fail(code, message, true);
            operationCoroutine = null;
            yield break;
        }

        SetState(SessionState.LocalTrackReady, "Audience microphone track is ready.");

        if (!CreatePeerConnectionAndAddLocalTrack())
        {
            Fail("PeerConnectionCreationFailed", "Could not create the Audience audio PeerConnection.", true);
            operationCoroutine = null;
            yield break;
        }

        RTCSessionDescriptionAsyncOperation offerOperation = audioPeerConnection.CreateOffer();
        yield return offerOperation;

        if (offerOperation.IsError)
        {
            Fail("CreateOfferFailed", offerOperation.Error.message, true);
            operationCoroutine = null;
            yield break;
        }

        RTCSessionDescription offer = offerOperation.Desc;
        RTCSetSessionDescriptionAsyncOperation localOperation =
            audioPeerConnection.SetLocalDescription(ref offer);
        yield return localOperation;

        if (localOperation.IsError)
        {
            Fail("SetLocalOfferFailed", localOperation.Error.message, true);
            operationCoroutine = null;
            yield break;
        }

        SessionSignal offerSignal = new SessionSignal
        {
            sessionId = activeSessionId,
            sdp = offer.sdp
        };

        SendJsonToOther(OfferType, JsonUtility.ToJson(offerSignal));
        SetState(SessionState.Negotiating, "Audience audio offer sent.");
        StartConnectionTimeout(activeSessionId);
        operationCoroutine = null;
    }

    private IEnumerator HandleOfferRoutine(SessionSignal signal)
    {
        if (role != EndpointRole.Actor ||
            signal == null ||
            string.IsNullOrEmpty(signal.sessionId) ||
            signal.sessionId != activeSessionId ||
            string.IsNullOrEmpty(signal.sdp))
        {
            yield break;
        }

        if (audioPeerConnection == null && !CreatePeerConnectionAndAddLocalTrack())
        {
            Fail("PeerConnectionMissing", "Actor audio PeerConnection is unavailable.", true);
            yield break;
        }

        RTCSessionDescription offer = new RTCSessionDescription
        {
            type = RTCSdpType.Offer,
            sdp = signal.sdp
        };

        RTCSetSessionDescriptionAsyncOperation remoteOperation =
            audioPeerConnection.SetRemoteDescription(ref offer);
        yield return remoteOperation;

        if (remoteOperation.IsError)
        {
            Fail("SetRemoteOfferFailed", remoteOperation.Error.message, true);
            yield break;
        }

        remoteDescriptionSet = true;
        FlushPendingRemoteCandidates();

        RTCSessionDescriptionAsyncOperation answerOperation = audioPeerConnection.CreateAnswer();
        yield return answerOperation;

        if (answerOperation.IsError)
        {
            Fail("CreateAnswerFailed", answerOperation.Error.message, true);
            yield break;
        }

        RTCSessionDescription answer = answerOperation.Desc;
        RTCSetSessionDescriptionAsyncOperation localOperation =
            audioPeerConnection.SetLocalDescription(ref answer);
        yield return localOperation;

        if (localOperation.IsError)
        {
            Fail("SetLocalAnswerFailed", localOperation.Error.message, true);
            yield break;
        }

        SendJsonToOther(
            AnswerType,
            JsonUtility.ToJson(new SessionSignal
            {
                sessionId = activeSessionId,
                sdp = answer.sdp
            })
        );

        SetState(SessionState.Connecting, "Actor audio answer sent; connecting ICE.");
    }

    private IEnumerator HandleAnswerRoutine(SessionSignal signal)
    {
        if (role != EndpointRole.Audience ||
            signal == null ||
            signal.sessionId != activeSessionId ||
            audioPeerConnection == null ||
            string.IsNullOrEmpty(signal.sdp))
        {
            yield break;
        }

        RTCSessionDescription answer = new RTCSessionDescription
        {
            type = RTCSdpType.Answer,
            sdp = signal.sdp
        };

        RTCSetSessionDescriptionAsyncOperation remoteOperation =
            audioPeerConnection.SetRemoteDescription(ref answer);
        yield return remoteOperation;

        if (remoteOperation.IsError)
        {
            Fail("SetRemoteAnswerFailed", remoteOperation.Error.message, true);
            yield break;
        }

        remoteDescriptionSet = true;
        FlushPendingRemoteCandidates();
        SetState(SessionState.Connecting, "Audience received the Actor answer; connecting ICE.");
    }

    private bool CreatePeerConnectionAndAddLocalTrack()
    {
        if (captureService == null || captureService.Track == null)
            return false;

        RTCConfiguration configuration = BuildRtcConfiguration();

        try
        {
            audioPeerConnection = new RTCPeerConnection(ref configuration);
            ConfigurePeerConnectionCallbacks();
            localAudioSender = audioPeerConnection.AddTrack(captureService.Track);
            return localAudioSender != null;
        }
        catch (Exception exception)
        {
            Debug.LogError(Prefix + "PeerConnection creation failed: " + exception);
            return false;
        }
    }

    private void ConfigurePeerConnectionCallbacks()
    {
        audioPeerConnection.OnIceCandidate = candidate =>
        {
            if (candidate == null || string.IsNullOrEmpty(candidate.Candidate) || string.IsNullOrEmpty(activeSessionId))
                return;

            IceSignal signal = new IceSignal
            {
                sessionId = activeSessionId,
                candidate = candidate.Candidate,
                sdpMid = candidate.SdpMid,
                sdpMLineIndex = candidate.SdpMLineIndex.HasValue
                    ? candidate.SdpMLineIndex.Value
                    : -1
            };

            SendJsonToOther(CandidateType, JsonUtility.ToJson(signal));
        };

        audioPeerConnection.OnTrack = trackEvent =>
        {
            if (trackEvent.Track is AudioStreamTrack audioTrack)
                AttachRemoteTrack(audioTrack);
        };

        audioPeerConnection.OnIceConnectionChange = state =>
        {
            Debug.Log(Prefix + "ICE state = " + state + ", Session = " + activeSessionId);

            iceConnected = state == RTCIceConnectionState.Connected ||
                           state == RTCIceConnectionState.Completed;

            if (iceConnected)
            {
                SetState(
                    remoteAudioTrack != null ? SessionState.Connected : SessionState.Connecting,
                    remoteAudioTrack != null
                        ? "ICE connected and remote audio track is playing."
                        : "ICE connected; waiting for the remote audio track."
                );
            }
            else if (state == RTCIceConnectionState.Failed)
            {
                Fail("IceFailed", "Audio ICE connection failed.", true);
            }
        };

        audioPeerConnection.OnConnectionStateChange = state =>
        {
            Debug.Log(Prefix + "PeerConnection state = " + state + ", Session = " + activeSessionId);

            if (state == RTCPeerConnectionState.Failed)
                Fail("PeerConnectionFailed", "Audio PeerConnection entered the Failed state.", true);
        };
    }

    private void AttachRemoteTrack(AudioStreamTrack audioTrack)
    {
        if (audioTrack == null)
            return;

        if (remoteAudioTrack != null && remoteAudioTrack != audioTrack)
            remoteAudioTrack.Dispose();

        remoteAudioTrack = audioTrack;
        EnsureAudioObjects();

        remotePlaybackAudioSource.SetTrack(remoteAudioTrack);
        remotePlaybackAudioSource.loop = true;
        remotePlaybackAudioSource.Play();

        if (FindFirstObjectByType<AudioListener>() == null)
        {
            Debug.LogError(Prefix + "No active AudioListener exists. Remote voice cannot be heard.");
        }

        SetState(
            iceConnected ? SessionState.Connected : SessionState.Connecting,
            iceConnected
                ? "Remote audio track received and playing."
                : "Remote audio track received; waiting for ICE connection."
        );
    }

    private void OnSignalReceived(PlayerRef from, string type, string payload)
    {
        if (type == DeviceListRequestType && role == EndpointRole.Audience)
        {
            SendAudienceDeviceList(from);
            return;
        }

        if (type == DeviceListType && role == EndpointRole.Actor)
        {
            DeviceListSignal signal = JsonUtility.FromJson<DeviceListSignal>(payload);
            if (signal != null)
            {
                AudienceMicrophoneListReceived?.Invoke(
                    signal.devices ?? Array.Empty<string>(),
                    signal.selectedDevice ?? ""
                );
            }
            return;
        }

        if (type == DeviceSelectType && role == EndpointRole.Audience)
        {
            DeviceSelectSignal request = JsonUtility.FromJson<DeviceSelectSignal>(payload);
            string error = "";
            bool success = request != null && captureService.SetSelectedDevice(request.deviceName, out error);

            if (request == null)
                error = "Invalid microphone selection payload.";

            DeviceSelectSignal acknowledgement = new DeviceSelectSignal
            {
                deviceName = request != null ? request.deviceName : "",
                success = success,
                message = success ? "Audience microphone selection applied." : error
            };

            SendJson(from, DeviceSelectAckType, JsonUtility.ToJson(acknowledgement));
            SendAudienceDeviceList(from);
            return;
        }

        if (type == DeviceSelectAckType && role == EndpointRole.Actor)
        {
            DeviceSelectSignal acknowledgement = JsonUtility.FromJson<DeviceSelectSignal>(payload);
            if (acknowledgement != null)
            {
                AudienceMicrophoneSelectionAcknowledged?.Invoke(
                    acknowledgement.deviceName ?? "",
                    acknowledgement.success,
                    acknowledgement.message ?? ""
                );
            }
            return;
        }

        if (type == StartRequestType && role == EndpointRole.Audience)
        {
            SessionSignal signal = JsonUtility.FromJson<SessionSignal>(payload);
            if (signal == null || string.IsNullOrEmpty(signal.sessionId))
                return;

            if (operationCoroutine != null)
                StopCoroutine(operationCoroutine);

            CleanupLocalSession("Preparing a new Audience audio session.", false);
            operationCoroutine = StartCoroutine(StartAudienceOfferRoutine(signal.sessionId));
            return;
        }

        if (type == StopType)
        {
            SessionSignal signal = JsonUtility.FromJson<SessionSignal>(payload);
            if (signal == null || string.IsNullOrEmpty(activeSessionId) || signal.sessionId == activeSessionId)
                CleanupLocalSession("Remote endpoint stopped the audio session.");
            return;
        }

        if (type == OfferType && role == EndpointRole.Actor)
        {
            StartCoroutine(HandleOfferRoutine(JsonUtility.FromJson<SessionSignal>(payload)));
            return;
        }

        if (type == AnswerType && role == EndpointRole.Audience)
        {
            StartCoroutine(HandleAnswerRoutine(JsonUtility.FromJson<SessionSignal>(payload)));
            return;
        }

        if (type == CandidateType)
        {
            HandleRemoteCandidate(JsonUtility.FromJson<IceSignal>(payload));
            return;
        }

        if ((type == ErrorType || type == StatusType) && role == EndpointRole.Actor)
        {
            SessionSignal signal = JsonUtility.FromJson<SessionSignal>(payload);
            if (signal != null && (string.IsNullOrEmpty(signal.sessionId) || signal.sessionId == activeSessionId))
            {
                if (type == ErrorType)
                    Fail(signal.errorCode, signal.message, false);
                else
                    StateChanged?.Invoke(State, signal.message ?? "");
            }
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

        if (audioPeerConnection == null || !remoteDescriptionSet)
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
        if (audioPeerConnection == null)
            return;

        RTCIceCandidateInit initialization = new RTCIceCandidateInit
        {
            candidate = signal.candidate,
            sdpMid = signal.sdpMid,
            sdpMLineIndex = signal.sdpMLineIndex >= 0 ? signal.sdpMLineIndex : null
        };

        audioPeerConnection.AddIceCandidate(new RTCIceCandidate(initialization));
    }

    private void SendAudienceDeviceList(PlayerRef target)
    {
        EnsureAudioObjects();

        DeviceListSignal signal = new DeviceListSignal
        {
            devices = captureService.GetDevices(),
            selectedDevice = captureService.SelectedDeviceName,
            endpointLabel = SystemInfo.deviceName + " / " + Application.platform
        };

        SendJson(target, DeviceListType, JsonUtility.ToJson(signal));
    }

    private bool SendJsonToOther(string type, string json)
    {
        if (WebRtcSignalHub.Instance == null)
        {
            Debug.LogWarning(Prefix + "Cannot send " + type + ": WebRtcSignalHub is unavailable.");
            return false;
        }

        PlayerRef target = WebRtcSignalHub.Instance.GetOtherPlayer();
        if (target == PlayerRef.None)
        {
            Debug.LogWarning(Prefix + "Cannot send " + type + ": no remote player is available.");
            return false;
        }

        SendJson(target, type, json);
        return true;
    }

    private static void SendJson(PlayerRef target, string type, string json)
    {
        if (WebRtcSignalHub.Instance != null)
            WebRtcSignalHub.Instance.SendSignal(target, type, json);
    }

    private void Fail(string code, string message, bool notifyRemote)
    {
        Debug.LogError(Prefix + code + ": " + message);
        string failedSessionId = activeSessionId;

        if (notifyRemote && !string.IsNullOrEmpty(failedSessionId))
        {
            SendJsonToOther(
                ErrorType,
                JsonUtility.ToJson(new SessionSignal
                {
                    sessionId = failedSessionId,
                    errorCode = code,
                    message = message
                })
            );
        }

        // Release the microphone and native WebRTC objects immediately on failure.
        // The currently running operation coroutine is allowed to return naturally,
        // avoiding the self-stop problem that can leave a start routine half-finished.
        operationCoroutine = null;
        connectionTimeoutCoroutine = null;
        ReleaseMediaResources();
        activeSessionId = "";
        SetState(SessionState.Failed, code + ": " + message);
    }

    private void StartConnectionTimeout(string sessionId)
    {
        if (connectionTimeoutCoroutine != null)
            StopCoroutine(connectionTimeoutCoroutine);

        connectionTimeoutCoroutine = StartCoroutine(ConnectionTimeoutRoutine(sessionId));
    }

    private IEnumerator ConnectionTimeoutRoutine(string sessionId)
    {
        yield return new WaitForSecondsRealtime(connectionTimeoutSeconds);

        if (sessionId == activeSessionId && State != SessionState.Connected)
            Fail("ConnectionTimeout", "Audio connection did not become ready before the timeout.", true);

        connectionTimeoutCoroutine = null;
    }

    private void CleanupLocalSession(string message, bool setIdle = true)
    {
        if (State != SessionState.Idle)
            SetState(SessionState.Stopping, message);

        if (operationCoroutine != null)
        {
            StopCoroutine(operationCoroutine);
            operationCoroutine = null;
        }

        if (connectionTimeoutCoroutine != null)
        {
            StopCoroutine(connectionTimeoutCoroutine);
            connectionTimeoutCoroutine = null;
        }

        ReleaseMediaResources();
        activeSessionId = "";

        if (setIdle)
            SetState(SessionState.Idle, message);
    }

    private void ReleaseMediaResources()
    {
        remoteDescriptionSet = false;
        iceConnected = false;
        pendingRemoteCandidates.Clear();

        if (remotePlaybackAudioSource != null)
        {
            remotePlaybackAudioSource.Stop();
            remotePlaybackAudioSource.clip = null;
        }

        if (audioPeerConnection != null)
        {
            if (localAudioSender != null)
            {
                audioPeerConnection.RemoveTrack(localAudioSender);
                localAudioSender = null;
            }

            audioPeerConnection.Close();
            audioPeerConnection.Dispose();
            audioPeerConnection = null;
        }

        if (remoteAudioTrack != null)
        {
            remoteAudioTrack.Dispose();
            remoteAudioTrack = null;
        }

        if (captureService != null)
            captureService.StopCapture();
    }

    private RTCConfiguration BuildRtcConfiguration()
    {
        List<RTCIceServer> servers = new List<RTCIceServer>();

        if (useStun && !string.IsNullOrWhiteSpace(stunUrl))
        {
            servers.Add(new RTCIceServer
            {
                urls = new[] { stunUrl }
            });
        }

        if (useTurn && (!string.IsNullOrWhiteSpace(turnUrlUdp) || !string.IsNullOrWhiteSpace(turnUrlTcp)))
        {
            List<string> urls = new List<string>();
            if (!string.IsNullOrWhiteSpace(turnUrlUdp))
                urls.Add(turnUrlUdp);
            if (!string.IsNullOrWhiteSpace(turnUrlTcp))
                urls.Add(turnUrlTcp);

            servers.Add(new RTCIceServer
            {
                urls = urls.ToArray(),
                username = turnUsername,
                credential = turnCredential
            });
        }

        return new RTCConfiguration
        {
            iceServers = servers.ToArray()
        };
    }

    private void EnsureAudioObjects()
    {
        if (captureService == null)
        {
            Transform existing = transform.Find("Local Microphone Capture");
            GameObject captureObject = existing != null
                ? existing.gameObject
                : new GameObject("Local Microphone Capture");

            if (existing == null)
                captureObject.transform.SetParent(transform, false);

            captureService = captureObject.GetComponent<MicrophoneCaptureService>();
            if (captureService == null)
                captureService = captureObject.AddComponent<MicrophoneCaptureService>();
        }

        if (remotePlaybackAudioSource == null)
        {
            Transform existing = transform.Find("Remote Voice Playback");
            GameObject playbackObject = existing != null
                ? existing.gameObject
                : new GameObject("Remote Voice Playback");

            if (existing == null)
                playbackObject.transform.SetParent(transform, false);

            remotePlaybackAudioSource = playbackObject.GetComponent<AudioSource>();
            if (remotePlaybackAudioSource == null)
                remotePlaybackAudioSource = playbackObject.AddComponent<AudioSource>();
        }

        remotePlaybackAudioSource.playOnAwake = false;
        remotePlaybackAudioSource.loop = true;
        remotePlaybackAudioSource.spatialBlend = 0f;
        remotePlaybackAudioSource.volume = 1f;
        remotePlaybackAudioSource.mute = false;
        remotePlaybackAudioSource.ignoreListenerPause = true;
    }

    private void SetState(SessionState state, string message)
    {
        State = state;
        Debug.Log(Prefix + "State = " + state + ". " + message);
        StateChanged?.Invoke(state, message);
    }

    private string Prefix => "[Audio][" + role + "] ";

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
        CleanupLocalSession("Audio endpoint disabled.");
    }

    private void OnDestroy()
    {
        if (signalHubSubscribed && WebRtcSignalHub.Instance != null)
            WebRtcSignalHub.Instance.OnSignalReceived -= OnSignalReceived;

        signalHubSubscribed = false;
        CleanupLocalSession("Audio endpoint destroyed.");
    }
}
