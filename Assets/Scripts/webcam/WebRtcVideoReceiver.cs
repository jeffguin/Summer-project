using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using Unity.WebRTC;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public class WebRtcVideoReceiver : MonoBehaviour
{
    [Header("Display")]
    [SerializeField] private VideoDisplayScreen videoDisplayScreen;

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

    private Coroutine webRtcUpdateCoroutine;

    private AudioStreamTrack localAudioTrack;
    private VideoStreamTrack remoteVideoTrack;
    private AudioStreamTrack remoteAudioTrack;

    private bool microphoneStartedByThisScript = false;
    private string activeMicrophoneDeviceName = null;

    private bool videoRemoteDescriptionSet = false;
    private bool audioRemoteDescriptionSet = false;

    private readonly Queue<string> pendingVideoCandidates = new Queue<string>();
    private readonly Queue<string> pendingAudioCandidates = new Queue<string>();

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

    private void Start()
    {
        Debug.Log("WebRtcVideoReceiver: Start running on " + Application.platform);

        if (videoDisplayScreen == null)
        {
            videoDisplayScreen = FindObjectOfType<VideoDisplayScreen>(true);

            if (videoDisplayScreen != null)
            {
                Debug.Log("WebRtcVideoReceiver: Auto-found VideoDisplayScreen on " + videoDisplayScreen.gameObject.name);
            }
            else
            {
                Debug.LogError("WebRtcVideoReceiver: VideoDisplayScreen is missing. Webcam image cannot be displayed.");
            }
        }
        else
        {
            Debug.Log("WebRtcVideoReceiver: VideoDisplayScreen assigned: " + videoDisplayScreen.gameObject.name);
        }

        webRtcUpdateCoroutine = StartCoroutine(WebRTC.Update());
        StartCoroutine(WaitForSignalHub());
    }

    private IEnumerator WaitForSignalHub()
    {
        Debug.Log("WebRtcVideoReceiver: Waiting for WebRtcSignalHub...");

        while (WebRtcSignalHub.Instance == null)
        {
            yield return null;
        }

        WebRtcSignalHub.Instance.OnSignalReceived -= OnSignalReceived;
        WebRtcSignalHub.Instance.OnSignalReceived += OnSignalReceived;

        Debug.Log("WebRtcVideoReceiver: Connected to WebRtcSignalHub.");
    }

    // =========================================================
    // Microphone device selection API for PerformerWebcamControlPanel
    // =========================================================

    public void SetMicrophoneDeviceName(string deviceName)
    {
        microphoneDeviceName = deviceName;

        Debug.Log(
            "WebRtcVideoReceiver: Actor microphone device selected: " +
            (string.IsNullOrEmpty(microphoneDeviceName) ? "Default" : microphoneDeviceName)
        );

        if (localAudioTrack != null || microphoneStartedByThisScript)
        {
            Debug.LogWarning(
                "WebRtcVideoReceiver: Microphone device changed while audio WebRTC is active. " +
                "The new device will be used after restarting the audio connection."
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

    // =========================================================
    // Video PeerConnection
    // =========================================================

    private void CreateVideoPeerConnection()
    {
        if (videoPeerConnection != null)
        {
            Debug.Log("WebRtcVideoReceiver: Video PeerConnection already exists.");
            return;
        }

        RTCConfiguration config = BuildRtcConfiguration();

        videoPeerConnection = new RTCPeerConnection(ref config);

        videoPeerConnection.OnIceCandidate = candidate =>
        {
            if (candidate == null || string.IsNullOrEmpty(candidate.Candidate))
                return;

            Debug.Log("WebRtcVideoReceiver: Local video ICE candidate: " + candidate.Candidate);
            LogCandidateType("WebRtcVideoReceiver video", candidate.Candidate);

            if (WebRtcSignalHub.Instance == null)
            {
                Debug.LogWarning("WebRtcVideoReceiver: SignalHub is null when sending video ICE candidate.");
                return;
            }

            PlayerRef target = WebRtcSignalHub.Instance.GetOtherPlayer();

            if (target == PlayerRef.None)
            {
                Debug.LogWarning("WebRtcVideoReceiver: No sender player found for video ICE candidate.");
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

            Debug.Log("WebRtcVideoReceiver: Sending video ICE candidate. Payload length: " + json.Length);

            WebRtcSignalHub.Instance.SendSignal(target, "candidate", json);
        };

        videoPeerConnection.OnTrack = e =>
        {
            Debug.Log("WebRtcVideoReceiver: Video PeerConnection OnTrack fired. Track kind: " + e.Track.Kind);

            VideoStreamTrack videoTrack = e.Track as VideoStreamTrack;

            if (videoTrack != null)
            {
                HandleRemoteVideoTrack(videoTrack);
                return;
            }

            Debug.LogWarning("WebRtcVideoReceiver: Video PeerConnection received unsupported track kind: " + e.Track.Kind);
        };

        videoPeerConnection.OnIceConnectionChange = state =>
        {
            Debug.Log("WebRtcVideoReceiver: Video ICE state: " + state);
        };

        videoPeerConnection.OnConnectionStateChange = state =>
        {
            Debug.Log("WebRtcVideoReceiver: Video connection state: " + state);
        };

        Debug.Log("WebRtcVideoReceiver: Video PeerConnection created.");
    }

    private void HandleRemoteVideoTrack(VideoStreamTrack videoTrack)
    {
        Debug.Log("WebRtcVideoReceiver: Remote video track received.");

        if (videoTrack == null)
        {
            Debug.LogWarning("WebRtcVideoReceiver: Remote video track is null.");
            return;
        }

        remoteVideoTrack = videoTrack;

        remoteVideoTrack.OnVideoReceived += texture =>
        {
            Debug.Log("WebRtcVideoReceiver: OnVideoReceived fired.");

            if (texture == null)
            {
                Debug.LogWarning("WebRtcVideoReceiver: Received null texture.");
                return;
            }

            Debug.Log(
                "WebRtcVideoReceiver: Remote texture received. " +
                "Size = " + texture.width + "x" + texture.height
            );

            if (videoDisplayScreen == null)
            {
                Debug.LogError("WebRtcVideoReceiver: videoDisplayScreen is null. Cannot display texture.");
                return;
            }

            videoDisplayScreen.SetTexture(texture);

            Debug.Log("WebRtcVideoReceiver: Texture sent to VideoDisplayScreen.");
        };

        Debug.Log("WebRtcVideoReceiver: OnVideoReceived callback registered.");
    }

    private IEnumerator HandleVideoOffer(PlayerRef from, string payload)
    {
        Debug.Log("WebRtcVideoReceiver: Handling video offer...");

        CreateVideoPeerConnection();

        if (videoPeerConnection == null)
        {
            Debug.LogError("WebRtcVideoReceiver: videoPeerConnection is null after CreateVideoPeerConnection.");
            yield break;
        }

        SdpSignal signal = JsonUtility.FromJson<SdpSignal>(payload);

        if (signal == null || string.IsNullOrEmpty(signal.sdp))
        {
            Debug.LogError("WebRtcVideoReceiver: Invalid video offer payload.");
            yield break;
        }

        RTCSessionDescription offer = new RTCSessionDescription
        {
            type = RTCSdpType.Offer,
            sdp = signal.sdp
        };

        RTCSetSessionDescriptionAsyncOperation remoteOp =
            videoPeerConnection.SetRemoteDescription(ref offer);

        yield return remoteOp;

        if (remoteOp.IsError)
        {
            Debug.LogError("WebRtcVideoReceiver: SetRemoteDescription video offer failed: " + remoteOp.Error.message);
            yield break;
        }

        Debug.Log("WebRtcVideoReceiver: Remote video offer applied.");

        videoRemoteDescriptionSet = true;
        FlushPendingVideoCandidates();

        RTCSessionDescriptionAsyncOperation answerOp =
            videoPeerConnection.CreateAnswer();

        yield return answerOp;

        if (answerOp.IsError)
        {
            Debug.LogError("WebRtcVideoReceiver: Create video answer failed: " + answerOp.Error.message);
            yield break;
        }

        RTCSessionDescription answer = answerOp.Desc;

        RTCSetSessionDescriptionAsyncOperation localOp =
            videoPeerConnection.SetLocalDescription(ref answer);

        yield return localOp;

        if (localOp.IsError)
        {
            Debug.LogError("WebRtcVideoReceiver: SetLocalDescription video answer failed: " + localOp.Error.message);
            yield break;
        }

        Debug.Log("WebRtcVideoReceiver: Local video answer applied.");

        SdpSignal answerSignal = new SdpSignal
        {
            sdp = answer.sdp
        };

        string json = JsonUtility.ToJson(answerSignal);

        Debug.Log("WebRtcVideoReceiver: Sending video answer. Payload length: " + json.Length);

        WebRtcSignalHub.Instance.SendSignal(from, "answer", json);

        Debug.Log("WebRtcVideoReceiver: WebRTC video answer sent.");
    }

    public void StopReceiving()
    {
        videoRemoteDescriptionSet = false;
        pendingVideoCandidates.Clear();

        if (videoDisplayScreen != null)
        {
            videoDisplayScreen.ClearTexture();
        }

        if (remoteVideoTrack != null)
        {
            remoteVideoTrack.Dispose();
            remoteVideoTrack = null;
        }

        if (videoPeerConnection != null)
        {
            videoPeerConnection.Close();
            videoPeerConnection.Dispose();
            videoPeerConnection = null;
        }

        Debug.Log("WebRtcVideoReceiver: Video receiver stopped.");
    }

    // =========================================================
    // Independent Audio PeerConnection
    // =========================================================

    private void CreateAudioPeerConnection()
    {
        if (audioPeerConnection != null)
        {
            Debug.Log("WebRtcVideoReceiver: Audio PeerConnection already exists.");
            return;
        }

        RTCConfiguration config = BuildRtcConfiguration();

        audioPeerConnection = new RTCPeerConnection(ref config);

        audioPeerConnection.OnIceCandidate = candidate =>
        {
            if (candidate == null || string.IsNullOrEmpty(candidate.Candidate))
                return;

            Debug.Log("WebRtcVideoReceiver: Local audio ICE candidate: " + candidate.Candidate);
            LogCandidateType("WebRtcVideoReceiver audio", candidate.Candidate);

            if (WebRtcSignalHub.Instance == null)
            {
                Debug.LogWarning("WebRtcVideoReceiver: SignalHub is null when sending audio ICE candidate.");
                return;
            }

            PlayerRef target = WebRtcSignalHub.Instance.GetOtherPlayer();

            if (target == PlayerRef.None)
            {
                Debug.LogWarning("WebRtcVideoReceiver: No sender player found for audio ICE candidate.");
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

            Debug.Log("WebRtcVideoReceiver: Sending audio ICE candidate. Payload length: " + json.Length);

            WebRtcSignalHub.Instance.SendSignal(target, "audio_candidate", json);
        };

        audioPeerConnection.OnTrack = e =>
        {
            Debug.Log("WebRtcVideoReceiver: Audio PeerConnection OnTrack fired. Track kind: " + e.Track.Kind);

            AudioStreamTrack audioTrack = e.Track as AudioStreamTrack;

            if (audioTrack != null)
            {
                HandleRemoteAudioTrack(audioTrack);
                return;
            }

            Debug.LogWarning("WebRtcVideoReceiver: Audio PeerConnection received unsupported track kind: " + e.Track.Kind);
        };

        audioPeerConnection.OnIceConnectionChange = state =>
        {
            Debug.Log("WebRtcVideoReceiver: Audio ICE state: " + state);
        };

        audioPeerConnection.OnConnectionStateChange = state =>
        {
            Debug.Log("WebRtcVideoReceiver: Audio connection state: " + state);
        };

        Debug.Log("WebRtcVideoReceiver: Audio PeerConnection created.");
    }

    private IEnumerator HandleAudioOffer(PlayerRef from, string payload)
    {
        Debug.Log("WebRtcVideoReceiver: Handling audio offer...");

        CleanupAudioConnection();

        CreateAudioPeerConnection();

        if (audioPeerConnection == null)
        {
            Debug.LogError("WebRtcVideoReceiver: audioPeerConnection is null after CreateAudioPeerConnection.");
            yield break;
        }

        SdpSignal signal = JsonUtility.FromJson<SdpSignal>(payload);

        if (signal == null || string.IsNullOrEmpty(signal.sdp))
        {
            Debug.LogError("WebRtcVideoReceiver: Invalid audio offer payload.");
            yield break;
        }

        RTCSessionDescription offer = new RTCSessionDescription
        {
            type = RTCSdpType.Offer,
            sdp = signal.sdp
        };

        RTCSetSessionDescriptionAsyncOperation remoteOp =
            audioPeerConnection.SetRemoteDescription(ref offer);

        yield return remoteOp;

        if (remoteOp.IsError)
        {
            Debug.LogError("WebRtcVideoReceiver: SetRemoteDescription audio offer failed: " + remoteOp.Error.message);
            yield break;
        }

        Debug.Log("WebRtcVideoReceiver: Remote audio offer applied.");

        audioRemoteDescriptionSet = true;
        FlushPendingAudioCandidates();

        if (enableMicrophone)
        {
            yield return StartLocalMicrophoneAndAddAudioTrack();
        }
        else
        {
            Debug.LogWarning("WebRtcVideoReceiver: Enable Microphone is false. Actor local audio track will not be added.");
        }

        RTCSessionDescriptionAsyncOperation answerOp =
            audioPeerConnection.CreateAnswer();

        yield return answerOp;

        if (answerOp.IsError)
        {
            Debug.LogError("WebRtcVideoReceiver: Create audio answer failed: " + answerOp.Error.message);
            yield break;
        }

        RTCSessionDescription answer = answerOp.Desc;

        RTCSetSessionDescriptionAsyncOperation localOp =
            audioPeerConnection.SetLocalDescription(ref answer);

        yield return localOp;

        if (localOp.IsError)
        {
            Debug.LogError("WebRtcVideoReceiver: SetLocalDescription audio answer failed: " + localOp.Error.message);
            yield break;
        }

        Debug.Log("WebRtcVideoReceiver: Local audio answer applied.");

        SdpSignal answerSignal = new SdpSignal
        {
            sdp = answer.sdp
        };

        string json = JsonUtility.ToJson(answerSignal);

        Debug.Log("WebRtcVideoReceiver: Sending audio answer. Payload length: " + json.Length);

        WebRtcSignalHub.Instance.SendSignal(from, "audio_answer", json);

        Debug.Log("WebRtcVideoReceiver: WebRTC audio answer sent.");
    }

    private IEnumerator StartLocalMicrophoneAndAddAudioTrack()
    {
        Debug.Log("WebRtcVideoReceiver: StartLocalMicrophoneAndAddAudioTrack called.");

        if (audioPeerConnection == null)
        {
            Debug.LogError("WebRtcVideoReceiver: audioPeerConnection is null. Cannot add local microphone track.");
            yield break;
        }

        if (localAudioTrack != null)
        {
            Debug.Log("WebRtcVideoReceiver: Local audio track already exists.");
            yield break;
        }

        Debug.Log("WebRtcVideoReceiver: enableMicrophone = " + enableMicrophone);
        Debug.Log("WebRtcVideoReceiver: Current microphoneDeviceName = " +
                  (string.IsNullOrEmpty(microphoneDeviceName) ? "Default" : microphoneDeviceName));

#if UNITY_ANDROID && !UNITY_EDITOR
    Debug.Log("WebRtcVideoReceiver: Android microphone permission before request = " +
              Permission.HasUserAuthorizedPermission(Permission.Microphone));

    if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
    {
        Debug.Log("WebRtcVideoReceiver: Requesting Android microphone permission...");

        Permission.RequestUserPermission(Permission.Microphone);

        float permissionTimeout = Time.time + 10f;

        while (!Permission.HasUserAuthorizedPermission(Permission.Microphone) &&
               Time.time < permissionTimeout)
        {
            yield return null;
        }

        Debug.Log("WebRtcVideoReceiver: Android microphone permission after request = " +
                  Permission.HasUserAuthorizedPermission(Permission.Microphone));

        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Debug.LogError("WebRtcVideoReceiver: Microphone permission was not granted.");
            yield break;
        }
    }
#endif

        string[] devices = Microphone.devices;

        Debug.Log("WebRtcVideoReceiver: Microphone.devices length = " +
                  (devices != null ? devices.Length : -1));

        if (devices != null)
        {
            for (int i = 0; i < devices.Length; i++)
            {
                Debug.Log("WebRtcVideoReceiver: Microphone device " + i + " = " + devices[i]);
            }
        }

        if (devices == null || devices.Length == 0)
        {
            Debug.LogError("WebRtcVideoReceiver: No microphone devices found.");
            yield break;
        }

        activeMicrophoneDeviceName =
            string.IsNullOrEmpty(microphoneDeviceName)
                ? devices[0]
                : microphoneDeviceName;

        Debug.Log("WebRtcVideoReceiver: Starting microphone: " + activeMicrophoneDeviceName);

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
            Debug.LogError("WebRtcVideoReceiver: Microphone.Start returned null AudioClip.");
            yield break;
        }

        Debug.Log(
            "WebRtcVideoReceiver: Microphone.Start returned clip. " +
            "ClipName = " + micClip.name +
            ", Frequency = " + micClip.frequency +
            ", Channels = " + micClip.channels +
            ", Samples = " + micClip.samples
        );

        float startTimeout = Time.time + 5f;

        while (Microphone.GetPosition(activeMicrophoneDeviceName) <= 0 &&
               Time.time < startTimeout)
        {
            yield return null;
        }

        int micPosition = Microphone.GetPosition(activeMicrophoneDeviceName);

        Debug.Log("WebRtcVideoReceiver: Microphone.GetPosition = " + micPosition);

        if (micPosition <= 0)
        {
            Debug.LogError("WebRtcVideoReceiver: Microphone did not start producing samples.");
            yield break;
        }

        localMicrophoneAudioSource.clip = micClip;
        localMicrophoneAudioSource.Play();

        Debug.Log("WebRtcVideoReceiver: localMicrophoneAudioSource.Play called. IsPlaying = " +
                  localMicrophoneAudioSource.isPlaying);

        localAudioTrack = new AudioStreamTrack(localMicrophoneAudioSource)
        {
            // Capture the microphone without playing it through the Quest speakers.
            Loopback = false
        };
        audioPeerConnection.AddTrack(localAudioTrack);

        Debug.Log("WebRtcVideoReceiver: Local microphone audio track added to audio PeerConnection.");
    }

    private void HandleRemoteAudioTrack(AudioStreamTrack audioTrack)
    {
        Debug.Log("WebRtcVideoReceiver: Remote audio track received.");

        if (audioTrack == null)
        {
            Debug.LogWarning("WebRtcVideoReceiver: Remote audio track is null.");
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

        Debug.Log("WebRtcVideoReceiver: Remote audio track attached to AudioSource.");
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
        Debug.Log("WebRtcVideoReceiver: Created isolated AudioSource: " + childName);
        return createdSource;
    }

    public void StopAudioReceiving()
    {
        Debug.Log("WebRtcVideoReceiver: StopAudioReceiving called.");
        CleanupAudioConnection();
    }

    private void CleanupAudioConnection()
    {
        audioRemoteDescriptionSet = false;
        pendingAudioCandidates.Clear();

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

        Debug.Log("WebRtcVideoReceiver: Audio connection cleaned up.");
    }

    // =========================================================
    // Signaling
    // =========================================================

    private void OnSignalReceived(PlayerRef from, string type, string payload)
    {
        Debug.Log(
            "WebRtcVideoReceiver: Signal received. " +
            "Type = " + type +
            ", From = " + from +
            ", PayloadLength = " + (payload != null ? payload.Length : 0)
        );

        if (type == "offer")
        {
            StartCoroutine(HandleVideoOffer(from, payload));
        }
        else if (type == "candidate")
        {
            HandleVideoCandidate(payload);
        }
        else if (type == "audio_offer")
        {
            StartCoroutine(HandleAudioOffer(from, payload));
        }
        else if (type == "audio_candidate")
        {
            HandleAudioCandidate(payload);
        }
        else if (type == "audio_stop")
        {
            StopAudioReceiving();
        }
    }

    private void HandleVideoCandidate(string payload)
    {
        Debug.Log("WebRtcVideoReceiver: Video candidate received. RemoteDescriptionSet = " + videoRemoteDescriptionSet);

        if (!videoRemoteDescriptionSet)
        {
            pendingVideoCandidates.Enqueue(payload);
            Debug.Log("WebRtcVideoReceiver: Video candidate queued. Queue count: " + pendingVideoCandidates.Count);
            return;
        }

        AddVideoRemoteCandidate(payload);
    }

    private void HandleAudioCandidate(string payload)
    {
        Debug.Log("WebRtcVideoReceiver: Audio candidate received. RemoteDescriptionSet = " + audioRemoteDescriptionSet);

        if (!audioRemoteDescriptionSet)
        {
            pendingAudioCandidates.Enqueue(payload);
            Debug.Log("WebRtcVideoReceiver: Audio candidate queued. Queue count: " + pendingAudioCandidates.Count);
            return;
        }

        AddAudioRemoteCandidate(payload);
    }

    private void FlushPendingVideoCandidates()
    {
        Debug.Log("WebRtcVideoReceiver: Flushing pending video candidates. Count: " + pendingVideoCandidates.Count);

        while (pendingVideoCandidates.Count > 0)
        {
            AddVideoRemoteCandidate(pendingVideoCandidates.Dequeue());
        }
    }

    private void FlushPendingAudioCandidates()
    {
        Debug.Log("WebRtcVideoReceiver: Flushing pending audio candidates. Count: " + pendingAudioCandidates.Count);

        while (pendingAudioCandidates.Count > 0)
        {
            AddAudioRemoteCandidate(pendingAudioCandidates.Dequeue());
        }
    }

    private void AddVideoRemoteCandidate(string payload)
    {
        if (videoPeerConnection == null)
        {
            Debug.LogWarning("WebRtcVideoReceiver: videoPeerConnection is null when adding video candidate.");
            return;
        }

        IceSignal signal = JsonUtility.FromJson<IceSignal>(payload);

        if (signal == null || string.IsNullOrEmpty(signal.candidate))
        {
            Debug.LogWarning("WebRtcVideoReceiver: Invalid video candidate payload.");
            return;
        }

        Debug.Log("WebRtcVideoReceiver: Remote video ICE candidate received: " + signal.candidate);
        LogCandidateType("WebRtcVideoReceiver remote video", signal.candidate);

        RTCIceCandidateInit init = new RTCIceCandidateInit
        {
            candidate = signal.candidate,
            sdpMid = signal.sdpMid,
            sdpMLineIndex = signal.sdpMLineIndex >= 0 ? signal.sdpMLineIndex : null
        };

        videoPeerConnection.AddIceCandidate(new RTCIceCandidate(init));

        Debug.Log("WebRtcVideoReceiver: Remote video ICE candidate added.");
    }

    private void AddAudioRemoteCandidate(string payload)
    {
        if (audioPeerConnection == null)
        {
            Debug.LogWarning("WebRtcVideoReceiver: audioPeerConnection is null when adding audio candidate.");
            return;
        }

        IceSignal signal = JsonUtility.FromJson<IceSignal>(payload);

        if (signal == null || string.IsNullOrEmpty(signal.candidate))
        {
            Debug.LogWarning("WebRtcVideoReceiver: Invalid audio candidate payload.");
            return;
        }

        Debug.Log("WebRtcVideoReceiver: Remote audio ICE candidate received: " + signal.candidate);
        LogCandidateType("WebRtcVideoReceiver remote audio", signal.candidate);

        RTCIceCandidateInit init = new RTCIceCandidateInit
        {
            candidate = signal.candidate,
            sdpMid = signal.sdpMid,
            sdpMLineIndex = signal.sdpMLineIndex >= 0 ? signal.sdpMLineIndex : null
        };

        audioPeerConnection.AddIceCandidate(new RTCIceCandidate(init));

        Debug.Log("WebRtcVideoReceiver: Remote audio ICE candidate added.");
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

            Debug.Log("WebRtcVideoReceiver: Google STUN enabled.");
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
                "WebRtcVideoReceiver: TURN enabled. " +
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
        if (WebRtcSignalHub.Instance != null)
        {
            WebRtcSignalHub.Instance.OnSignalReceived -= OnSignalReceived;
        }

        StopReceiving();
        CleanupAudioConnection();

        if (webRtcUpdateCoroutine != null)
        {
            StopCoroutine(webRtcUpdateCoroutine);
            webRtcUpdateCoroutine = null;
        }
    }
}
