using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.UI;

public class WebRtcWebcamSender : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LocalWebcamManager localWebcamManager;

    [Header("Optional Local Test UI")]
    [SerializeField] private Button startStreamButton;

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

    private RTCPeerConnection peerConnection;
    private VideoStreamTrack videoTrack;
    private Coroutine startRoutine;

    private bool remoteDescriptionSet = false;
    private bool isStreaming = false;
    private bool isStarting = false;

    private readonly List<IceSignal> pendingRemoteCandidates = new List<IceSignal>();

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
        Debug.Log("WebRtcWebcamSender: Start running on " + Application.platform);

        WebRtcRuntimePump.EnsureExists();

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

        WebRtcSignalHub.Instance.OnSignalReceived += OnSignalReceived;

        Debug.Log("WebRtcWebcamSender: Connected to WebRtcSignalHub.");
    }

    public void StartWebcamStream()
    {
        Debug.Log("WebRtcWebcamSender: StartWebcamStream called.");

        if (isStarting || isStreaming)
        {
            Debug.LogWarning("WebRtcWebcamSender: Already starting or streaming. Start ignored.");
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

        if (peerConnection != null)
        {
            Debug.LogWarning("WebRtcWebcamSender: Existing peerConnection found. Resetting before start.");
            StopWebcamStream();
        }

        startRoutine = StartCoroutine(StartSenderRoutine(target, webcamTexture));
    }

    private IEnumerator StartSenderRoutine(PlayerRef target, WebCamTexture webcamTexture)
    {
        isStarting = true;
        remoteDescriptionSet = false;
        pendingRemoteCandidates.Clear();

        CreatePeerConnection();

        if (peerConnection == null)
        {
            Debug.LogError("WebRtcWebcamSender: Failed to create peerConnection.");
            isStarting = false;
            yield break;
        }

        videoTrack = new VideoStreamTrack(webcamTexture);
        peerConnection.AddTrack(videoTrack);

        Debug.Log("WebRtcWebcamSender: Video track added.");

        RTCSessionDescriptionAsyncOperation offerOp = peerConnection.CreateOffer();

        yield return offerOp;

        if (offerOp.IsError)
        {
            Debug.LogError("WebRtcWebcamSender: CreateOffer failed: " + offerOp.Error.message);
            isStarting = false;
            StopWebcamStream();
            yield break;
        }

        RTCSessionDescription offer = offerOp.Desc;

        RTCSetSessionDescriptionAsyncOperation localOp =
            peerConnection.SetLocalDescription(ref offer);

        yield return localOp;

        if (localOp.IsError)
        {
            Debug.LogError("WebRtcWebcamSender: SetLocalDescription offer failed: " + localOp.Error.message);
            isStarting = false;
            StopWebcamStream();
            yield break;
        }

        Debug.Log("WebRtcWebcamSender: Local offer applied.");

        SdpSignal signal = new SdpSignal
        {
            sdp = offer.sdp
        };

        string json = JsonUtility.ToJson(signal);

        Debug.Log("WebRtcWebcamSender: Sending offer. Payload length: " + json.Length);

        WebRtcSignalHub.Instance.SendSignal(target, "video.offer", json);

        isStarting = false;
        isStreaming = true;

        Debug.Log("WebRtcWebcamSender: WebRTC offer sent.");
    }

    public void StopWebcamStream()
    {
        if (startRoutine != null)
        {
            StopCoroutine(startRoutine);
            startRoutine = null;
        }

        isStarting = false;
        isStreaming = false;
        remoteDescriptionSet = false;
        pendingRemoteCandidates.Clear();

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

        Debug.Log("WebRtcWebcamSender: Stream stopped.");
    }

    private void CreatePeerConnection()
    {
        RTCConfiguration config = BuildRtcConfiguration();

        peerConnection = new RTCPeerConnection(ref config);

        peerConnection.OnIceCandidate = candidate =>
        {
            if (candidate == null || string.IsNullOrEmpty(candidate.Candidate))
                return;

            Debug.Log("WebRtcWebcamSender: Local ICE candidate: " + candidate.Candidate);
            LogCandidateType("WebRtcWebcamSender", candidate.Candidate);

            if (WebRtcSignalHub.Instance == null)
            {
                Debug.LogWarning("WebRtcWebcamSender: SignalHub is null when sending ICE candidate.");
                return;
            }

            PlayerRef target = WebRtcSignalHub.Instance.GetOtherPlayer();

            if (target == PlayerRef.None)
            {
                Debug.LogWarning("WebRtcWebcamSender: No receiver player found for ICE candidate.");
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

            Debug.Log("WebRtcWebcamSender: Sending ICE candidate. Payload length: " + json.Length);

            WebRtcSignalHub.Instance.SendSignal(target, "video.ice", json);
        };

        peerConnection.OnTrack = e =>
        {
            Debug.Log("WebRtcWebcamSender: OnTrack fired. Track kind: " + e.Track.Kind);

            Debug.LogWarning(
                "WebRtcWebcamSender: Video PeerConnection received an unexpected remote track: " +
                e.Track.Kind
            );
        };

        peerConnection.OnIceConnectionChange = state =>
        {
            Debug.Log("WebRtcWebcamSender: ICE state: " + state);
        };

        peerConnection.OnConnectionStateChange = state =>
        {
            Debug.Log("WebRtcWebcamSender: Connection state: " + state);
        };

        Debug.Log("WebRtcWebcamSender: PeerConnection created.");
    }

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

    private void OnSignalReceived(PlayerRef from, string type, string payload)
    {
        Debug.Log(
            "WebRtcWebcamSender: Signal received. " +
            "Type = " + type +
            ", From = " + from +
            ", PayloadLength = " + (payload != null ? payload.Length : 0)
        );

        if (type == "video.answer")
        {
            StartCoroutine(HandleAnswer(payload));
        }
        else if (type == "video.ice")
        {
            HandleRemoteIceCandidate(payload);
        }
    }

    private IEnumerator HandleAnswer(string payload)
    {
        if (peerConnection == null)
        {
            Debug.LogWarning("WebRtcWebcamSender: peerConnection is null when answer received.");
            yield break;
        }

        SdpSignal signal = JsonUtility.FromJson<SdpSignal>(payload);

        if (signal == null || string.IsNullOrEmpty(signal.sdp))
        {
            Debug.LogWarning("WebRtcWebcamSender: Invalid answer payload.");
            yield break;
        }

        RTCSessionDescription answer = new RTCSessionDescription
        {
            type = RTCSdpType.Answer,
            sdp = signal.sdp
        };

        RTCSetSessionDescriptionAsyncOperation remoteOp =
            peerConnection.SetRemoteDescription(ref answer);

        yield return remoteOp;

        if (remoteOp.IsError)
        {
            Debug.LogError("WebRtcWebcamSender: SetRemoteDescription answer failed: " + remoteOp.Error.message);
            yield break;
        }

        remoteDescriptionSet = true;

        Debug.Log("WebRtcWebcamSender: WebRTC answer applied.");

        FlushPendingRemoteCandidates();
    }

    private void HandleRemoteIceCandidate(string payload)
    {
        IceSignal signal = JsonUtility.FromJson<IceSignal>(payload);

        if (signal == null || string.IsNullOrEmpty(signal.candidate))
        {
            Debug.LogWarning("WebRtcWebcamSender: Invalid ICE candidate payload.");
            return;
        }

        Debug.Log("WebRtcWebcamSender: Remote ICE candidate received: " + signal.candidate);
        LogCandidateType("WebRtcWebcamSender remote", signal.candidate);

        if (!remoteDescriptionSet || peerConnection == null)
        {
            pendingRemoteCandidates.Add(signal);
            Debug.Log("WebRtcWebcamSender: Remote ICE candidate cached. Count = " + pendingRemoteCandidates.Count);
            return;
        }

        AddRemoteIceCandidate(signal);
    }

    private void FlushPendingRemoteCandidates()
    {
        if (peerConnection == null)
            return;

        if (pendingRemoteCandidates.Count == 0)
            return;

        Debug.Log("WebRtcWebcamSender: Flushing pending ICE candidates. Count: " + pendingRemoteCandidates.Count);

        foreach (IceSignal signal in pendingRemoteCandidates)
        {
            AddRemoteIceCandidate(signal);
        }

        pendingRemoteCandidates.Clear();
    }

    private void AddRemoteIceCandidate(IceSignal signal)
    {
        if (peerConnection == null)
            return;

        RTCIceCandidateInit init = new RTCIceCandidateInit
        {
            candidate = signal.candidate,
            sdpMid = signal.sdpMid,
            sdpMLineIndex = signal.sdpMLineIndex >= 0 ? signal.sdpMLineIndex : null
        };

        peerConnection.AddIceCandidate(new RTCIceCandidate(init));

        Debug.Log("WebRtcWebcamSender: Remote ICE candidate added.");
    }

    private void OnDestroy()
    {
        if (WebRtcSignalHub.Instance != null)
        {
            WebRtcSignalHub.Instance.OnSignalReceived -= OnSignalReceived;
        }

        StopWebcamStream();

    }
}
