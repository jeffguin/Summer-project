using System;
using System.Collections;
using System.Collections.Generic;
using Unity.WebRTC;
using UnityEngine;
using Fusion;


public class WebRtcVideoReceiver : MonoBehaviour
{
    [Header("Display")]
    [SerializeField] private VideoDisplayScreen videoDisplayScreen;

    private RTCPeerConnection peerConnection;
    private Coroutine webRtcUpdateCoroutine;

    private bool remoteDescriptionSet = false;
    private readonly Queue<string> pendingCandidates = new Queue<string>();

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


    public void StopReceiving()
    {
        remoteDescriptionSet = false;
        pendingCandidates.Clear();

        if (videoDisplayScreen != null)
        {
            videoDisplayScreen.ClearTexture();
        }

        if (peerConnection != null)
        {
            peerConnection.Close();
            peerConnection.Dispose();
            peerConnection = null;
        }

        Debug.Log("WebRTC receiver stopped.");
    }

    private void Start()
    {
        webRtcUpdateCoroutine = StartCoroutine(WebRTC.Update());
        StartCoroutine(WaitForSignalHub());
    }

    private IEnumerator WaitForSignalHub()
    {
        while (WebRtcSignalHub.Instance == null)
        {
            yield return null;
        }

        WebRtcSignalHub.Instance.OnSignalReceived += OnSignalReceived;
        Debug.Log("Receiver connected to WebRtcSignalHub.");
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

        peerConnection.OnTrack = e =>
        {
            VideoStreamTrack videoTrack = e.Track as VideoStreamTrack;

            if (videoTrack == null)
                return;

            videoTrack.OnVideoReceived += texture =>
            {
                if (videoDisplayScreen != null)
                {
                    videoDisplayScreen.SetTexture(texture);
                }

                Debug.Log("Receiver got remote video texture.");
            };
        };

        peerConnection.OnIceConnectionChange = state =>
        {
            Debug.Log("Receiver ICE state: " + state);
        };

        peerConnection.OnConnectionStateChange = state =>
        {
            Debug.Log("Receiver connection state: " + state);
        };

        Debug.Log("Receiver PeerConnection created.");
    }

    private void OnSignalReceived(PlayerRef from, string type, string payload)
    {
        if (type == "offer")
        {
            StartCoroutine(HandleOffer(from, payload));
        }
        else if (type == "candidate")
        {
            HandleCandidate(payload);
        }
    }

    private IEnumerator HandleOffer(PlayerRef from, string payload)
    {
        CreatePeerConnection();

        SdpSignal signal = JsonUtility.FromJson<SdpSignal>(payload);

        RTCSessionDescription offer = new RTCSessionDescription
        {
            type = RTCSdpType.Offer,
            sdp = signal.sdp
        };

        RTCSetSessionDescriptionAsyncOperation remoteOp =
            peerConnection.SetRemoteDescription(ref offer);

        yield return remoteOp;

        if (remoteOp.IsError)
        {
            Debug.LogError("SetRemoteDescription offer failed: " + remoteOp.Error.message);
            yield break;
        }

        remoteDescriptionSet = true;
        FlushPendingCandidates();

        RTCSessionDescriptionAsyncOperation answerOp =
            peerConnection.CreateAnswer();

        yield return answerOp;

        if (answerOp.IsError)
        {
            Debug.LogError("CreateAnswer failed: " + answerOp.Error.message);
            yield break;
        }

        RTCSessionDescription answer = answerOp.Desc;

        RTCSetSessionDescriptionAsyncOperation localOp =
            peerConnection.SetLocalDescription(ref answer);

        yield return localOp;

        if (localOp.IsError)
        {
            Debug.LogError("SetLocalDescription answer failed: " + localOp.Error.message);
            yield break;
        }

        SdpSignal answerSignal = new SdpSignal
        {
            sdp = answer.sdp
        };

        string json = JsonUtility.ToJson(answerSignal);
        WebRtcSignalHub.Instance.SendSignal(from, "answer", json);

        Debug.Log("WebRTC answer sent.");
    }

    private void HandleCandidate(string payload)
    {
        if (!remoteDescriptionSet)
        {
            pendingCandidates.Enqueue(payload);
            return;
        }

        AddRemoteCandidate(payload);
    }

    private void FlushPendingCandidates()
    {
        while (pendingCandidates.Count > 0)
        {
            AddRemoteCandidate(pendingCandidates.Dequeue());
        }
    }

    private void AddRemoteCandidate(string payload)
    {
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

        StopReceiving();

        if (webRtcUpdateCoroutine != null)
        {
            StopCoroutine(webRtcUpdateCoroutine);
        }
    }
}