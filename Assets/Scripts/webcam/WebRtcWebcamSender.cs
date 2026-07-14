using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public class WebRtcWebcamSender : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LocalWebcamManager localWebcamManager;

    [Header("Optional Local Test UI")]
    [SerializeField] private Button startStreamButton;

    [Header("Local Microphone Sending")]
    [SerializeField] private bool enableMicrophone = true;

    [Tooltip("Leave empty to use the default microphone.")]
    [SerializeField] private string microphoneDeviceName = "";

    [SerializeField] private int microphoneSampleRate = 48000;
    [SerializeField] private int microphoneClipLengthSeconds = 1;
    [SerializeField] private AudioSource localMicrophoneAudioSource;

    [Header("Remote Audio Playback")]
    [SerializeField] private AudioSource remoteAudioSource;

    [Header("ICE / STUN / TURN Settings")]
    [SerializeField] private bool useGoogleStun = true;

    [Tooltip("如果你已经有 TURN 服务器，就打开这个。")]
    [SerializeField] private bool useTurn = false;

    [Tooltip("例如：turn:123.123.123.123:3478?transport=udp")]
    [SerializeField] private string turnUrlUdp = "turn:YOUR_TURN_SERVER:3478?transport=udp";

    [Tooltip("例如：turn:123.123.123.123:3478?transport=tcp")]
    [SerializeField] private string turnUrlTcp = "turn:YOUR_TURN_SERVER:3478?transport=tcp";

    [SerializeField] private string turnUsername = "YOUR_USERNAME";
    [SerializeField] private string turnCredential = "YOUR_PASSWORD";

    private RTCPeerConnection videoPeerConnection;
    private RTCPeerConnection audioPeerConnection;

    private VideoStreamTrack videoTrack;
    private AudioStreamTrack localAudioTrack;
    private AudioStreamTrack remoteAudioTrack;

    private Coroutine webRtcUpdateCoroutine;
    private Coroutine startVideoRoutine;
    private Coroutine startAudioRoutine;

    private bool videoRemoteDescriptionSet = false;
    private bool audioRemoteDescriptionSet = false;

    private bool isVideoStreaming = false;
    private bool isVideoStarting = false;

    private bool isAudioStreaming = false;
    private bool isAudioStarting = false;

    private bool microphoneStartedByThisScript = false;
    private string activeMicrophoneDeviceName = null;

    private readonly List<IceSignal> pendingVideoRemoteCandidates = new List<IceSignal>();
    private readonly List<IceSignal> pendingAudioRemoteCandidates = new List<IceSignal>();

    [Serializable]
    private class SdpSignal
    {
        public string sdp;
    }

    [Serializable]
    private class IceSignal
    {
        public string candidate;
        public string sdpMid;
        public int sdpMLineIndex;
    }

    [Serializable]
    private class MicrophoneDeviceListSignal
    {
        public string[] devices;
        public string selectedDevice;
    }

    [Serializable]
    private class MicrophoneDeviceSelectSignal
    {
        public string deviceName;
    }

    private void Start()
    {
        Debug.Log("WebRtcWebcamSender: Start running on " + Application.platform);

        webRtcUpdateCoroutine = StartCoroutine(WebRTC.Update());

        if (startStreamButton != null)
        {
            startStreamButton.onClick.AddListener(StartWebcamStream);
        }

        StartCoroutine(WaitForSignalHub());
    }

    private IEnumerator WaitForSignalHub()
    {
        Debug.Log("WebRtcWebcamSender: Waiting for WebRtcSignalHub...");

        while (WebRtcSignalHub.Instance == null)
        {
            yield return null;
        }

        WebRtcSignalHub.Instance.OnSignalReceived -= OnSignalReceived;
        WebRtcSignalHub.Instance.OnSignalReceived += OnSignalReceived;

        Debug.Log("WebRtcWebcamSender: Connected to WebRtcSignalHub.");
    }

    // =========================================================
    // Microphone device selection API for remote Actor menu
    // =========================================================

    public void SetMicrophoneDeviceName(string deviceName)
    {
        microphoneDeviceName = deviceName;

        Debug.Log(
            "WebRtcWebcamSender: Audience microphone device selected: " +
            (string.IsNullOrEmpty(microphoneDeviceName) ? "Default" : microphoneDeviceName)
        );

        if (isAudioStreaming || isAudioStarting || localAudioTrack != null)
        {
            Debug.LogWarning(
                "WebRtcWebcamSender: Microphone device changed while audio WebRTC is active. " +
                "The new device will be used after restarting the audio stream."
            );
        }
    }

    public string GetMicrophoneDeviceName()
    {
        return microphoneDeviceName;
    }

    public string[] GetLocalMicrophoneDevices()
    {
        if (Microphone.devices == null)
        {
            return Array.Empty<string>();
        }

        return Microphone.devices;
    }

    private void SendAudienceMicrophoneDeviceList(PlayerRef target)
    {
        if (WebRtcSignalHub.Instance == null)
        {
            Debug.LogWarning("WebRtcWebcamSender: Cannot send microphone list. SignalHub is null.");
            return;
        }

        string[] devices = GetLocalMicrophoneDevices();

        MicrophoneDeviceListSignal signal = new MicrophoneDeviceListSignal
        {
            devices = devices,
            selectedDevice = microphoneDeviceName
        };

        string json = JsonUtility.ToJson(signal);

        Debug.Log(
            "WebRtcWebcamSender: Sending audience microphone device list. " +
            "Count = " + devices.Length +
            ", Selected = " + (string.IsNullOrEmpty(microphoneDeviceName) ? "Default" : microphoneDeviceName) +
            ", PayloadLength = " + json.Length
        );

        WebRtcSignalHub.Instance.SendSignal(target, "audience_mic_list", json);
    }

    private void HandleAudienceMicrophoneSelection(string payload)
    {
        MicrophoneDeviceSelectSignal signal =
            JsonUtility.FromJson<MicrophoneDeviceSelectSignal>(payload);

        if (signal == null)
        {
            Debug.LogWarning("WebRtcWebcamSender: Invalid audience microphone selection payload.");
            return;
        }

        string requestedDevice = signal.deviceName;

        if (!string.IsNullOrEmpty(requestedDevice))
        {
            string[] devices = GetLocalMicrophoneDevices();

            bool exists = false;

            foreach (string device in devices)
            {
                if (device == requestedDevice)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                Debug.LogWarning(
                    "WebRtcWebcamSender: Requested microphone device does not exist on Audience PC: " +
                    requestedDevice
                );

                return;
            }
        }

        SetMicrophoneDeviceName(requestedDevice);
    }

    // =========================================================
    // Video Stream
    // =========================================================

    public void StartWebcamStream()
    {
        Debug.Log("WebRtcWebcamSender: StartWebcamStream called.");

        if (isVideoStarting || isVideoStreaming)
        {
            Debug.LogWarning("WebRtcWebcamSender: Video already starting or streaming. Start ignored.");
            return;
        }

        if (WebRtcSignalHub.Instance == null)
        {
            Debug.LogWarning("WebRtcWebcamSender: SignalHub is not ready.");
            return;
        }

        PlayerRef target = WebRtcSignalHub.Instance.GetOtherPlayer();

        if (target == PlayerRef.None)
        {
            Debug.LogWarning("WebRtcWebcamSender: No receiver player found.");
            return;
        }

        if (localWebcamManager == null)
        {
            Debug.LogWarning("WebRtcWebcamSender: LocalWebcamManager is not assigned.");
            return;
        }

        WebCamTexture webcamTexture = localWebcamManager.GetCurrentWebcamTexture();

        if (webcamTexture == null || !webcamTexture.isPlaying)
        {
            Debug.LogWarning("WebRtcWebcamSender: WebcamTexture is null or not playing. Start local webcam first.");
            return;
        }

        Debug.Log(
            "WebRtcWebcamSender: Using webcam texture. " +
            "Name = " + webcamTexture.deviceName +
            ", Size = " + webcamTexture.width + "x" + webcamTexture.height
        );

        if (videoPeerConnection != null)
        {
            Debug.LogWarning("WebRtcWebcamSender: Existing video peerConnection found. Resetting before start.");
            StopWebcamStream();
        }

        startVideoRoutine = StartCoroutine(StartVideoSenderRoutine(target, webcamTexture));
    }

    private IEnumerator StartVideoSenderRoutine(PlayerRef target, WebCamTexture webcamTexture)
    {
        isVideoStarting = true;
        videoRemoteDescriptionSet = false;
        pendingVideoRemoteCandidates.Clear();

        CreateVideoPeerConnection();

        if (videoPeerConnection == null)
        {
            Debug.LogError("WebRtcWebcamSender: Failed to create videoPeerConnection.");
            isVideoStarting = false;
            yield break;
        }

        videoTrack = new VideoStreamTrack(webcamTexture);
        videoPeerConnection.AddTrack(videoTrack);

        Debug.Log("WebRtcWebcamSender: Video track added.");

        RTCSessionDescriptionAsyncOperation offerOp = videoPeerConnection.CreateOffer();

        yield return offerOp;

        if (offerOp.IsError)
        {
            Debug.LogError("WebRtcWebcamSender: Create video offer failed: " + offerOp.Error.message);
            isVideoStarting = false;
            StopWebcamStream();
            yield break;
        }

        RTCSessionDescription offer = offerOp.Desc;

        RTCSetSessionDescriptionAsyncOperation localOp =
            videoPeerConnection.SetLocalDescription(ref offer);

        yield return localOp;

        if (localOp.IsError)
        {
            Debug.LogError("WebRtcWebcamSender: SetLocalDescription video offer failed: " + localOp.Error.message);
            isVideoStarting = false;
            StopWebcamStream();
            yield break;
        }

        Debug.Log("WebRtcWebcamSender: Local video offer applied.");

        SdpSignal signal = new SdpSignal
        {
            sdp = offer.sdp
        };

        string json = JsonUtility.ToJson(signal);

        Debug.Log("WebRtcWebcamSender: Sending video offer. Payload length: " + json.Length);

        WebRtcSignalHub.Instance.SendSignal(target, "offer", json);

        isVideoStarting = false;
        isVideoStreaming = true;

        Debug.Log("WebRtcWebcamSender: WebRTC video offer sent.");
    }

    public void StopWebcamStream()
    {
        if (startVideoRoutine != null)
        {
            StopCoroutine(startVideoRoutine);
            startVideoRoutine = null;
        }

        isVideoStarting = false;
        isVideoStreaming = false;
        videoRemoteDescriptionSet = false;
        pendingVideoRemoteCandidates.Clear();

        if (videoTrack != null)
        {
            videoTrack.Dispose();
            videoTrack = null;
        }

        if (videoPeerConnection != null)
        {
            videoPeerConnection.Close();
            videoPeerConnection.Dispose();
            videoPeerConnection = null;
        }

        Debug.Log("WebRtcWebcamSender: Video stream stopped.");
    }

    private void CreateVideoPeerConnection()
    {
        RTCConfiguration config = BuildRtcConfiguration();

        videoPeerConnection = new RTCPeerConnection(ref config);

        videoPeerConnection.OnIceCandidate = candidate =>
        {
            if (candidate == null || string.IsNullOrEmpty(candidate.Candidate))
                return;

            Debug.Log("WebRtcWebcamSender: Local video ICE candidate: " + candidate.Candidate);
            LogCandidateType("WebRtcWebcamSender video", candidate.Candidate);

            if (WebRtcSignalHub.Instance == null)
            {
                Debug.LogWarning("WebRtcWebcamSender: SignalHub is null when sending video ICE candidate.");
                return;
            }

            PlayerRef target = WebRtcSignalHub.Instance.GetOtherPlayer();

            if (target == PlayerRef.None)
            {
                Debug.LogWarning("WebRtcWebcamSender: No receiver player found for video ICE candidate.");
                return;
            }

            IceSignal signal = new IceSignal
            {
                candidate = candidate.Candidate,
                sdpMid = candidate.SdpMid,
                sdpMLineIndex = candidate.SdpMLineIndex.HasValue
                    ? candidate.SdpMLineIndex.Value
                    : -1
            };

            string json = JsonUtility.ToJson(signal);

            Debug.Log("WebRtcWebcamSender: Sending video ICE candidate. Payload length: " + json.Length);

            WebRtcSignalHub.Instance.SendSignal(target, "candidate", json);
        };

        videoPeerConnection.OnIceConnectionChange = state =>
        {
            Debug.Log("WebRtcWebcamSender: Video ICE state: " + state);
        };

        videoPeerConnection.OnConnectionStateChange = state =>
        {
            Debug.Log("WebRtcWebcamSender: Video connection state: " + state);
        };

        Debug.Log("WebRtcWebcamSender: Video PeerConnection created.");
    }

    // =========================================================
    // Independent Audio Stream
    // Called by NetworkWebcamControlHub through SendMessage.
    // =========================================================

    public void StartAudioStream()
    {
        Debug.Log("WebRtcWebcamSender: StartAudioStream called.");

        if (isAudioStarting || isAudioStreaming)
        {
            Debug.LogWarning("WebRtcWebcamSender: Audio already starting or streaming. Start ignored.");
            return;
        }

        if (!enableMicrophone)
        {
            Debug.LogWarning("WebRtcWebcamSender: Enable Microphone is false. Audio start aborted.");
            return;
        }

        if (WebRtcSignalHub.Instance == null)
        {
            Debug.LogWarning("WebRtcWebcamSender: SignalHub is not ready for audio.");
            return;
        }

        PlayerRef target = WebRtcSignalHub.Instance.GetOtherPlayer();

        if (target == PlayerRef.None)
        {
            Debug.LogWarning("WebRtcWebcamSender: No receiver player found for audio.");
            return;
        }

        if (audioPeerConnection != null)
        {
            Debug.LogWarning("WebRtcWebcamSender: Existing audio peerConnection found. Resetting before start.");
            StopAudioStream();
        }

        startAudioRoutine = StartCoroutine(StartAudioSenderRoutine(target));
    }

    private IEnumerator StartAudioSenderRoutine(PlayerRef target)
    {
        isAudioStarting = true;
        audioRemoteDescriptionSet = false;
        pendingAudioRemoteCandidates.Clear();

        CreateAudioPeerConnection();

        if (audioPeerConnection == null)
        {
            Debug.LogError("WebRtcWebcamSender: Failed to create audioPeerConnection.");
            isAudioStarting = false;
            yield break;
        }

        yield return StartLocalMicrophoneAndAddAudioTrack();

        if (localAudioTrack == null)
        {
            Debug.LogError("WebRtcWebcamSender: Local audio track was not created. Audio offer aborted.");
            isAudioStarting = false;
            StopAudioStream();
            yield break;
        }

        RTCSessionDescriptionAsyncOperation offerOp = audioPeerConnection.CreateOffer();

        yield return offerOp;

        if (offerOp.IsError)
        {
            Debug.LogError("WebRtcWebcamSender: Create audio offer failed: " + offerOp.Error.message);
            isAudioStarting = false;
            StopAudioStream();
            yield break;
        }

        RTCSessionDescription offer = offerOp.Desc;

        RTCSetSessionDescriptionAsyncOperation localOp =
            audioPeerConnection.SetLocalDescription(ref offer);

        yield return localOp;

        if (localOp.IsError)
        {
            Debug.LogError("WebRtcWebcamSender: SetLocalDescription audio offer failed: " + localOp.Error.message);
            isAudioStarting = false;
            StopAudioStream();
            yield break;
        }

        Debug.Log("WebRtcWebcamSender: Local audio offer applied.");

        SdpSignal signal = new SdpSignal
        {
            sdp = offer.sdp
        };

        string json = JsonUtility.ToJson(signal);

        Debug.Log("WebRtcWebcamSender: Sending audio offer. Payload length: " + json.Length);

        WebRtcSignalHub.Instance.SendSignal(target, "audio_offer", json);

        isAudioStarting = false;
        isAudioStreaming = true;

        Debug.Log("WebRtcWebcamSender: WebRTC audio offer sent.");
    }

    public void StopAudioStream()
    {
        Debug.Log("WebRtcWebcamSender: StopAudioStream called.");

        if (WebRtcSignalHub.Instance != null)
        {
            PlayerRef target = WebRtcSignalHub.Instance.GetOtherPlayer();

            if (target != PlayerRef.None)
            {
                WebRtcSignalHub.Instance.SendSignal(target, "audio_stop", "{}");
            }
        }

        CleanupAudioConnection();

        Debug.Log("WebRtcWebcamSender: Audio stream stopped.");
    }

    private void CleanupAudioConnection()
    {
        if (startAudioRoutine != null)
        {
            StopCoroutine(startAudioRoutine);
            startAudioRoutine = null;
        }

        isAudioStarting = false;
        isAudioStreaming = false;
        audioRemoteDescriptionSet = false;
        pendingAudioRemoteCandidates.Clear();

        if (localAudioTrack != null)
        {
            localAudioTrack.Dispose();
            localAudioTrack = null;
        }

        if (remoteAudioTrack != null)
        {
            remoteAudioTrack.Dispose();
            remoteAudioTrack = null;
        }

        if (localMicrophoneAudioSource != null)
        {
            localMicrophoneAudioSource.Stop();
            localMicrophoneAudioSource.clip = null;
        }

        if (microphoneStartedByThisScript &&
            !string.IsNullOrEmpty(activeMicrophoneDeviceName))
        {
            Microphone.End(activeMicrophoneDeviceName);
        }

        microphoneStartedByThisScript = false;
        activeMicrophoneDeviceName = null;

        if (remoteAudioSource != null)
        {
            remoteAudioSource.Stop();
            remoteAudioSource.clip = null;
        }

        if (audioPeerConnection != null)
        {
            audioPeerConnection.Close();
            audioPeerConnection.Dispose();
            audioPeerConnection = null;
        }
    }

    private void CreateAudioPeerConnection()
    {
        RTCConfiguration config = BuildRtcConfiguration();

        audioPeerConnection = new RTCPeerConnection(ref config);

        audioPeerConnection.OnIceCandidate = candidate =>
        {
            if (candidate == null || string.IsNullOrEmpty(candidate.Candidate))
                return;

            Debug.Log("WebRtcWebcamSender: Local audio ICE candidate: " + candidate.Candidate);
            LogCandidateType("WebRtcWebcamSender audio", candidate.Candidate);

            if (WebRtcSignalHub.Instance == null)
            {
                Debug.LogWarning("WebRtcWebcamSender: SignalHub is null when sending audio ICE candidate.");
                return;
            }

            PlayerRef target = WebRtcSignalHub.Instance.GetOtherPlayer();

            if (target == PlayerRef.None)
            {
                Debug.LogWarning("WebRtcWebcamSender: No receiver player found for audio ICE candidate.");
                return;
            }

            IceSignal signal = new IceSignal
            {
                candidate = candidate.Candidate,
                sdpMid = candidate.SdpMid,
                sdpMLineIndex = candidate.SdpMLineIndex.HasValue
                    ? candidate.SdpMLineIndex.Value
                    : -1
            };

            string json = JsonUtility.ToJson(signal);

            Debug.Log("WebRtcWebcamSender: Sending audio ICE candidate. Payload length: " + json.Length);

            WebRtcSignalHub.Instance.SendSignal(target, "audio_candidate", json);
        };

        audioPeerConnection.OnTrack = e =>
        {
            Debug.Log("WebRtcWebcamSender: Audio PeerConnection OnTrack fired. Track kind: " + e.Track.Kind);

            AudioStreamTrack audioTrack = e.Track as AudioStreamTrack;

            if (audioTrack != null)
            {
                Debug.Log("WebRtcWebcamSender: Remote audio track received from Actor.");
                AttachRemoteAudioTrack(audioTrack);
                return;
            }

            Debug.LogWarning("WebRtcWebcamSender: Audio PeerConnection received unsupported track kind: " + e.Track.Kind);
        };

        audioPeerConnection.OnIceConnectionChange = state =>
        {
            Debug.Log("WebRtcWebcamSender: Audio ICE state: " + state);
        };

        audioPeerConnection.OnConnectionStateChange = state =>
        {
            Debug.Log("WebRtcWebcamSender: Audio connection state: " + state);
        };

        Debug.Log("WebRtcWebcamSender: Audio PeerConnection created.");
    }

    private IEnumerator StartLocalMicrophoneAndAddAudioTrack()
    {
        if (audioPeerConnection == null)
            yield break;

        if (localAudioTrack != null)
        {
            Debug.Log("WebRtcWebcamSender: Local audio track already exists.");
            yield break;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Debug.Log("WebRtcWebcamSender: Requesting Android microphone permission...");

            Permission.RequestUserPermission(Permission.Microphone);

            float permissionTimeout = Time.time + 5f;

            while (!Permission.HasUserAuthorizedPermission(Permission.Microphone) &&
                   Time.time < permissionTimeout)
            {
                yield return null;
            }

            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Debug.LogWarning("WebRtcWebcamSender: Microphone permission was not granted.");
                yield break;
            }
        }
#endif

        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            Debug.LogWarning("WebRtcWebcamSender: No microphone devices found.");
            yield break;
        }

        activeMicrophoneDeviceName =
            string.IsNullOrEmpty(microphoneDeviceName)
                ? Microphone.devices[0]
                : microphoneDeviceName;

        Debug.Log("WebRtcWebcamSender: Starting microphone: " + activeMicrophoneDeviceName);

        localMicrophoneAudioSource = GetOrCreateIsolatedAudioSource(
            localMicrophoneAudioSource,
            remoteAudioSource,
            "Local Microphone Audio"
        );

        localMicrophoneAudioSource.playOnAwake = false;
        localMicrophoneAudioSource.loop = true;
        localMicrophoneAudioSource.spatialBlend = 0f;
        localMicrophoneAudioSource.volume = 1f;

        AudioClip micClip = Microphone.Start(
            activeMicrophoneDeviceName,
            true,
            microphoneClipLengthSeconds,
            microphoneSampleRate
        );

        microphoneStartedByThisScript = true;

        if (micClip == null)
        {
            Debug.LogWarning("WebRtcWebcamSender: Microphone.Start returned null AudioClip.");
            yield break;
        }

        float startTimeout = Time.time + 3f;

        while (Microphone.GetPosition(activeMicrophoneDeviceName) <= 0 &&
               Time.time < startTimeout)
        {
            yield return null;
        }

        if (Microphone.GetPosition(activeMicrophoneDeviceName) <= 0)
        {
            Debug.LogWarning("WebRtcWebcamSender: Microphone did not start producing samples.");
            yield break;
        }

        localMicrophoneAudioSource.clip = micClip;
        localMicrophoneAudioSource.Play();

        localAudioTrack = new AudioStreamTrack(localMicrophoneAudioSource)
        {
            // WebRTC captures this AudioSource without routing it back to the local speakers.
            Loopback = false
        };
        audioPeerConnection.AddTrack(localAudioTrack);

        Debug.Log("WebRtcWebcamSender: Local microphone audio track added to audio PeerConnection.");
    }

    private void AttachRemoteAudioTrack(AudioStreamTrack audioTrack)
    {
        if (audioTrack == null)
        {
            Debug.LogWarning("WebRtcWebcamSender: Remote audio track is null.");
            return;
        }

        remoteAudioTrack = audioTrack;

        remoteAudioSource = GetOrCreateIsolatedAudioSource(
            remoteAudioSource,
            localMicrophoneAudioSource,
            "Remote Audio Playback"
        );

        remoteAudioSource.playOnAwake = false;
        remoteAudioSource.loop = true;
        remoteAudioSource.spatialBlend = 0f;
        remoteAudioSource.volume = 1f;

        remoteAudioSource.SetTrack(remoteAudioTrack);
        remoteAudioSource.Play();

        Debug.Log("WebRtcWebcamSender: Remote audio track attached to AudioSource.");
    }

    private AudioSource GetOrCreateIsolatedAudioSource(
        AudioSource current,
        AudioSource sourceThatMustRemainSeparate,
        string childName)
    {
        if (current != null &&
            (sourceThatMustRemainSeparate == null || current.gameObject != sourceThatMustRemainSeparate.gameObject))
        {
            return current;
        }

        GameObject audioObject = new GameObject(childName);
        audioObject.transform.SetParent(transform, false);

        AudioSource createdSource = audioObject.AddComponent<AudioSource>();
        Debug.Log("WebRtcWebcamSender: Created isolated AudioSource: " + childName);
        return createdSource;
    }

    // =========================================================
    // Signaling
    // =========================================================

    private void OnSignalReceived(PlayerRef from, string type, string payload)
    {
        Debug.Log(
            "WebRtcWebcamSender: Signal received. " +
            "Type = " + type +
            ", From = " + from +
            ", PayloadLength = " + (payload != null ? payload.Length : 0)
        );

        if (type == "answer")
        {
            StartCoroutine(HandleVideoAnswer(payload));
        }
        else if (type == "candidate")
        {
            HandleVideoRemoteIceCandidate(payload);
        }
        else if (type == "audio_answer")
        {
            StartCoroutine(HandleAudioAnswer(payload));
        }
        else if (type == "audio_candidate")
        {
            HandleAudioRemoteIceCandidate(payload);
        }
        else if (type == "audio_start_request")
        {
            StartAudioStream();
        }
        else if (type == "audio_stop_request")
        {
            StopAudioStream();
        }
        else if (type == "audience_mic_list_request")
        {
            SendAudienceMicrophoneDeviceList(from);
        }
        else if (type == "audience_mic_select")
        {
            HandleAudienceMicrophoneSelection(payload);
        }
    }

    private IEnumerator HandleVideoAnswer(string payload)
    {
        if (videoPeerConnection == null)
        {
            Debug.LogWarning("WebRtcWebcamSender: videoPeerConnection is null when video answer received.");
            yield break;
        }

        SdpSignal signal = JsonUtility.FromJson<SdpSignal>(payload);

        if (signal == null || string.IsNullOrEmpty(signal.sdp))
        {
            Debug.LogWarning("WebRtcWebcamSender: Invalid video answer payload.");
            yield break;
        }

        RTCSessionDescription answer = new RTCSessionDescription
        {
            type = RTCSdpType.Answer,
            sdp = signal.sdp
        };

        RTCSetSessionDescriptionAsyncOperation remoteOp =
            videoPeerConnection.SetRemoteDescription(ref answer);

        yield return remoteOp;

        if (remoteOp.IsError)
        {
            Debug.LogError("WebRtcWebcamSender: SetRemoteDescription video answer failed: " + remoteOp.Error.message);
            yield break;
        }

        videoRemoteDescriptionSet = true;

        Debug.Log("WebRtcWebcamSender: WebRTC video answer applied.");

        FlushPendingVideoRemoteCandidates();
    }

    private IEnumerator HandleAudioAnswer(string payload)
    {
        if (audioPeerConnection == null)
        {
            Debug.LogWarning("WebRtcWebcamSender: audioPeerConnection is null when audio answer received.");
            yield break;
        }

        SdpSignal signal = JsonUtility.FromJson<SdpSignal>(payload);

        if (signal == null || string.IsNullOrEmpty(signal.sdp))
        {
            Debug.LogWarning("WebRtcWebcamSender: Invalid audio answer payload.");
            yield break;
        }

        RTCSessionDescription answer = new RTCSessionDescription
        {
            type = RTCSdpType.Answer,
            sdp = signal.sdp
        };

        RTCSetSessionDescriptionAsyncOperation remoteOp =
            audioPeerConnection.SetRemoteDescription(ref answer);

        yield return remoteOp;

        if (remoteOp.IsError)
        {
            Debug.LogError("WebRtcWebcamSender: SetRemoteDescription audio answer failed: " + remoteOp.Error.message);
            yield break;
        }

        audioRemoteDescriptionSet = true;

        Debug.Log("WebRtcWebcamSender: WebRTC audio answer applied.");

        FlushPendingAudioRemoteCandidates();
    }

    private void HandleVideoRemoteIceCandidate(string payload)
    {
        IceSignal signal = JsonUtility.FromJson<IceSignal>(payload);

        if (signal == null || string.IsNullOrEmpty(signal.candidate))
        {
            Debug.LogWarning("WebRtcWebcamSender: Invalid video ICE candidate payload.");
            return;
        }

        Debug.Log("WebRtcWebcamSender: Remote video ICE candidate received: " + signal.candidate);
        LogCandidateType("WebRtcWebcamSender remote video", signal.candidate);

        if (!videoRemoteDescriptionSet || videoPeerConnection == null)
        {
            pendingVideoRemoteCandidates.Add(signal);
            Debug.Log("WebRtcWebcamSender: Remote video ICE candidate cached. Count = " + pendingVideoRemoteCandidates.Count);
            return;
        }

        AddVideoRemoteIceCandidate(signal);
    }

    private void HandleAudioRemoteIceCandidate(string payload)
    {
        IceSignal signal = JsonUtility.FromJson<IceSignal>(payload);

        if (signal == null || string.IsNullOrEmpty(signal.candidate))
        {
            Debug.LogWarning("WebRtcWebcamSender: Invalid audio ICE candidate payload.");
            return;
        }

        Debug.Log("WebRtcWebcamSender: Remote audio ICE candidate received: " + signal.candidate);
        LogCandidateType("WebRtcWebcamSender remote audio", signal.candidate);

        if (!audioRemoteDescriptionSet || audioPeerConnection == null)
        {
            pendingAudioRemoteCandidates.Add(signal);
            Debug.Log("WebRtcWebcamSender: Remote audio ICE candidate cached. Count = " + pendingAudioRemoteCandidates.Count);
            return;
        }

        AddAudioRemoteIceCandidate(signal);
    }

    private void FlushPendingVideoRemoteCandidates()
    {
        if (videoPeerConnection == null)
            return;

        if (pendingVideoRemoteCandidates.Count == 0)
            return;

        Debug.Log("WebRtcWebcamSender: Flushing pending video ICE candidates. Count: " + pendingVideoRemoteCandidates.Count);

        foreach (IceSignal signal in pendingVideoRemoteCandidates)
        {
            AddVideoRemoteIceCandidate(signal);
        }

        pendingVideoRemoteCandidates.Clear();
    }

    private void FlushPendingAudioRemoteCandidates()
    {
        if (audioPeerConnection == null)
            return;

        if (pendingAudioRemoteCandidates.Count == 0)
            return;

        Debug.Log("WebRtcWebcamSender: Flushing pending audio ICE candidates. Count: " + pendingAudioRemoteCandidates.Count);

        foreach (IceSignal signal in pendingAudioRemoteCandidates)
        {
            AddAudioRemoteIceCandidate(signal);
        }

        pendingAudioRemoteCandidates.Clear();
    }

    private void AddVideoRemoteIceCandidate(IceSignal signal)
    {
        if (videoPeerConnection == null)
            return;

        RTCIceCandidateInit init = new RTCIceCandidateInit
        {
            candidate = signal.candidate,
            sdpMid = signal.sdpMid,
            sdpMLineIndex = signal.sdpMLineIndex >= 0 ? signal.sdpMLineIndex : null
        };

        videoPeerConnection.AddIceCandidate(new RTCIceCandidate(init));

        Debug.Log("WebRtcWebcamSender: Remote video ICE candidate added.");
    }

    private void AddAudioRemoteIceCandidate(IceSignal signal)
    {
        if (audioPeerConnection == null)
            return;

        RTCIceCandidateInit init = new RTCIceCandidateInit
        {
            candidate = signal.candidate,
            sdpMid = signal.sdpMid,
            sdpMLineIndex = signal.sdpMLineIndex >= 0 ? signal.sdpMLineIndex : null
        };

        audioPeerConnection.AddIceCandidate(new RTCIceCandidate(init));

        Debug.Log("WebRtcWebcamSender: Remote audio ICE candidate added.");
    }

    // =========================================================
    // Shared
    // =========================================================

    private RTCConfiguration BuildRtcConfiguration()
    {
        List<RTCIceServer> iceServers = new List<RTCIceServer>();

        if (useGoogleStun)
        {
            iceServers.Add(new RTCIceServer
            {
                urls = new[]
                {
                    "stun:stun.relay.metered.ca:80"
                }
            });

            Debug.Log("WebRtcWebcamSender: Google STUN enabled.");
        }

        if (useTurn)
        {
            iceServers.Add(new RTCIceServer
            {
                urls = new[]
                {
                    turnUrlUdp,
                    turnUrlTcp
                },
                username = turnUsername,
                credential = turnCredential
            });

            Debug.Log(
                "WebRtcWebcamSender: TURN enabled. " +
                "UDP = " + turnUrlUdp + ", TCP = " + turnUrlTcp
            );
        }

        RTCConfiguration config = new RTCConfiguration
        {
            iceServers = iceServers.ToArray()
        };

        return config;
    }

    private static void LogCandidateType(string prefix, string candidate)
    {
        if (candidate.Contains(" typ relay "))
        {
            Debug.Log(prefix + ": Candidate type = relay / TURN");
        }
        else if (candidate.Contains(" typ srflx "))
        {
            Debug.Log(prefix + ": Candidate type = srflx / STUN");
        }
        else if (candidate.Contains(" typ host "))
        {
            Debug.Log(prefix + ": Candidate type = host / local");
        }
        else
        {
            Debug.Log(prefix + ": Candidate type = unknown");
        }
    }

    private void OnDestroy()
    {
        if (startStreamButton != null)
        {
            startStreamButton.onClick.RemoveListener(StartWebcamStream);
        }

        if (WebRtcSignalHub.Instance != null)
        {
            WebRtcSignalHub.Instance.OnSignalReceived -= OnSignalReceived;
        }

        StopWebcamStream();
        CleanupAudioConnection();

        if (webRtcUpdateCoroutine != null)
        {
            StopCoroutine(webRtcUpdateCoroutine);
            webRtcUpdateCoroutine = null;
        }
    }
}
