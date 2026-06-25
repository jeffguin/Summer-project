using System;
using System.Collections;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.UI;
using Fusion;


public class WebRtcWebcamSender : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LocalWebcamManager localWebcamManager;
    [SerializeField] private Button startStreamButton;

    private RTCPeerConnection peerConnection;
    private VideoStreamTrack videoTrack;
    private Coroutine webRtcUpdateCoroutine;

    private bool remoteDescriptionSet = false;

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
        webRtcUpdateCoroutine = StartCoroutine(WebRTC.Update());

        if (startStreamButton != null)
        {
            startStreamButton.onClick.AddListener(StartWebcamStream);
        }

        StartCoroutine(WaitForSignalHub());
    }

    private IEnumerator WaitForSignalHub()
    {
        while (WebRtcSignalHub.Instance == null)
        {
            yield return null;
        }

        WebRtcSignalHub.Instance.OnSignalReceived += OnSignalReceived;
        Debug.Log("Sender connected to WebRtcSignalHub.");
    }

    public void StartWebcamStream()
    {
        if (WebRtcSignalHub.Instance == null)
        {
            Debug.LogWarning("SignalHub is not ready.");
            return;
        }

        PlayerRef target = WebRtcSignalHub.Instance.GetOtherPlayer();

        if (target == PlayerRef.None)
        {
            Debug.LogWarning("No actor client found. Join actor first.");
            return;
        }

        WebCamTexture webcamTexture = localWebcamManager.GetCurrentWebcamTexture();

        if (webcamTexture == null || !webcamTexture.isPlaying)
        {
            Debug.LogWarning("Start local webcam first.");
            return;
        }

        StartCoroutine(StartSenderRoutine(target, webcamTexture));
    }

    private IEnumerator StartSenderRoutine(PlayerRef target, WebCamTexture webcamTexture)
    {
        CreatePeerConnection();

        videoTrack = new VideoStreamTrack(webcamTexture);
        peerConnection.AddTrack(videoTrack);

        RTCSessionDescriptionAsyncOperation offerOp = peerConnection.CreateOffer();
        yield return offerOp;

        if (offerOp.IsError)
        {
            Debug.LogError("CreateOffer failed: " + offerOp.Error.message);
            yield break;
        }

        RTCSessionDescription offer = offerOp.Desc;

        RTCSetSessionDescriptionAsyncOperation localOp =
            peerConnection.SetLocalDescription(ref offer);

        yield return localOp;

        if (localOp.IsError)
        {
            Debug.LogError("SetLocalDescription offer failed: " + localOp.Error.message);
            yield break;
        }

        SdpSignal signal = new SdpSignal
        {
            sdp = offer.sdp
        };

        string json = JsonUtility.ToJson(signal);
        WebRtcSignalHub.Instance.SendSignal(target, "offer", json);

        Debug.Log("WebRTC offer sent.");
    }

    private void CreatePeerConnection()
    {
        if (peerConnection != null)
            return;

        RTCConfiguration config = default;

        peerConnection = new RTCPeerConnection(ref config);

        peerConnection.OnIceCandidate = candidate =>
        {
            if (candidate == null || string.IsNullOrEmpty(candidate.Candidate))
                return;

            PlayerRef target = WebRtcSignalHub.Instance.GetOtherPlayer();

            if (target == PlayerRef.None)
                return;

            IceSignal signal = new IceSignal
            {
                candidate = candidate.Candidate,
                sdpMid = candidate.SdpMid,
                sdpMLineIndex = candidate.SdpMLineIndex.HasValue
                    ? candidate.SdpMLineIndex.Value
                    : -1
            };

            string json = JsonUtility.ToJson(signal);
            WebRtcSignalHub.Instance.SendSignal(target, "candidate", json);
        };

        peerConnection.OnIceConnectionChange = state =>
        {
            Debug.Log("Sender ICE state: " + state);
        };

        peerConnection.OnConnectionStateChange = state =>
        {
            Debug.Log("Sender connection state: " + state);
        };

        Debug.Log("Sender PeerConnection created.");
    }

    private void OnSignalReceived(PlayerRef from, string type, string payload)
    {
        if (type == "answer")
        {
            StartCoroutine(HandleAnswer(payload));
        }
        else if (type == "candidate")
        {
            AddRemoteIceCandidate(payload);
        }
    }

    private IEnumerator HandleAnswer(string payload)
    {
        SdpSignal signal = JsonUtility.FromJson<SdpSignal>(payload);

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
            Debug.LogError("SetRemoteDescription answer failed: " + remoteOp.Error.message);
            yield break;
        }

        remoteDescriptionSet = true;

        Debug.Log("WebRTC answer applied.");
    }

    private void AddRemoteIceCandidate(string payload)
    {
        if (!remoteDescriptionSet)
            return;

        IceSignal signal = JsonUtility.FromJson<IceSignal>(payload);

        RTCIceCandidateInit init = new RTCIceCandidateInit
        {
            candidate = signal.candidate,
            sdpMid = signal.sdpMid,
            sdpMLineIndex = signal.sdpMLineIndex >= 0 ? signal.sdpMLineIndex : null
        };

        peerConnection.AddIceCandidate(new RTCIceCandidate(init));
    }

    private void OnDestroy()
    {
        if (WebRtcSignalHub.Instance != null)
        {
            WebRtcSignalHub.Instance.OnSignalReceived -= OnSignalReceived;
        }

        if (videoTrack != null)
        {
            videoTrack.Dispose();
        }

        if (peerConnection != null)
        {
            peerConnection.Close();
            peerConnection.Dispose();
        }

        if (webRtcUpdateCoroutine != null)
        {
            StopCoroutine(webRtcUpdateCoroutine);
        }
    }
}