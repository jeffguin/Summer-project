using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using Unity.WebRTC;
using UnityEngine;

public class WebRtcVideoReceiver : MonoBehaviour
{
    [Header("Display")]
    [SerializeField] private VideoDisplayScreen videoDisplayScreen;

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

    private VideoStreamTrack remoteVideoTrack;

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

    private void Start()
    {
        Debug.Log("WebRtcVideoReceiver: Start running on " + Application.platform);

        if (videoDisplayScreen == null)
        {
            videoDisplayScreen =
                FindFirstObjectByType<VideoDisplayScreen>(FindObjectsInactive.Include);

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

        WebRtcRuntimePump.EnsureExists();
        StartCoroutine(WaitForSignalHub());
    }

    private IEnumerator WaitForSignalHub()
    {
        Debug.Log("WebRtcVideoReceiver: Waiting for WebRtcSignalHub...");

        while (WebRtcSignalHub.Instance == null)
        {
            yield return null;
        }

        WebRtcSignalHub.Instance.OnSignalReceived += OnSignalReceived;

        Debug.Log("WebRtcVideoReceiver: Connected to WebRtcSignalHub.");
    }

    private void CreatePeerConnection()
    {
        if (peerConnection != null)
        {
            Debug.Log("WebRtcVideoReceiver: PeerConnection already exists.");
            return;
        }

        RTCConfiguration config = BuildRtcConfiguration();

        peerConnection = new RTCPeerConnection(ref config);

        peerConnection.OnIceCandidate = candidate =>
        {
            if (candidate == null || string.IsNullOrEmpty(candidate.Candidate))
                return;

            Debug.Log("WebRtcVideoReceiver: Local ICE candidate: " + candidate.Candidate);
            LogCandidateType("WebRtcVideoReceiver", candidate.Candidate);

            if (WebRtcSignalHub.Instance == null)
            {
                Debug.LogWarning("WebRtcVideoReceiver: SignalHub is null when sending ICE candidate.");
                return;
            }

            PlayerRef target = WebRtcSignalHub.Instance.GetOtherPlayer();

            if (target == PlayerRef.None)
            {
                Debug.LogWarning("WebRtcVideoReceiver: No sender player found for ICE candidate.");
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

            Debug.Log("WebRtcVideoReceiver: Sending ICE candidate. Payload length: " + json.Length);

            WebRtcSignalHub.Instance.SendSignal(target, "video.ice", json);
        };

        peerConnection.OnTrack = e =>
        {
            Debug.Log("WebRtcVideoReceiver: OnTrack fired. Track kind: " + e.Track.Kind);

            VideoStreamTrack videoTrack = e.Track as VideoStreamTrack;

            if (videoTrack != null)
            {
                HandleRemoteVideoTrack(videoTrack);
                return;
            }

            Debug.LogWarning("WebRtcVideoReceiver: Received unsupported remote track kind: " + e.Track.Kind);
        };

        peerConnection.OnIceConnectionChange = state =>
        {
            Debug.Log("WebRtcVideoReceiver: ICE state: " + state);
        };

        peerConnection.OnConnectionStateChange = state =>
        {
            Debug.Log("WebRtcVideoReceiver: Connection state: " + state);
        };

        Debug.Log("WebRtcVideoReceiver: PeerConnection created.");
    }

    private void HandleRemoteVideoTrack(VideoStreamTrack videoTrack)
    {
        Debug.Log("WebRtcVideoReceiver: Remote video track received.");

        if (videoTrack == null)
        {
            Debug.LogWarning("WebRtcVideoReceiver: Remote video track is null.");
            return;
        }

        // Important: keep a member reference to the remote track.
        // Without this, OnVideoReceived can be unreliable in Unity WebRTC.
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

    private void OnSignalReceived(PlayerRef from, string type, string payload)
    {
        Debug.Log(
            "WebRtcVideoReceiver: Signal received. " +
            "Type = " + type +
            ", From = " + from +
            ", PayloadLength = " + (payload != null ? payload.Length : 0)
        );

        if (type == "video.offer")
        {
            StartCoroutine(HandleOffer(from, payload));
        }
        else if (type == "video.ice")
        {
            HandleCandidate(payload);
        }
    }

    private IEnumerator HandleOffer(PlayerRef from, string payload)
    {
        Debug.Log("WebRtcVideoReceiver: Handling offer...");

        CreatePeerConnection();

        if (peerConnection == null)
        {
            Debug.LogError("WebRtcVideoReceiver: peerConnection is null after CreatePeerConnection.");
            yield break;
        }

        SdpSignal signal = JsonUtility.FromJson<SdpSignal>(payload);

        if (signal == null || string.IsNullOrEmpty(signal.sdp))
        {
            Debug.LogError("WebRtcVideoReceiver: Invalid offer payload.");
            yield break;
        }

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
            Debug.LogError("WebRtcVideoReceiver: SetRemoteDescription offer failed: " + remoteOp.Error.message);
            yield break;
        }

        Debug.Log("WebRtcVideoReceiver: Remote offer applied.");

        remoteDescriptionSet = true;
        FlushPendingCandidates();

        RTCSessionDescriptionAsyncOperation answerOp =
            peerConnection.CreateAnswer();

        yield return answerOp;

        if (answerOp.IsError)
        {
            Debug.LogError("WebRtcVideoReceiver: CreateAnswer failed: " + answerOp.Error.message);
            yield break;
        }

        RTCSessionDescription answer = answerOp.Desc;

        RTCSetSessionDescriptionAsyncOperation localOp =
            peerConnection.SetLocalDescription(ref answer);

        yield return localOp;

        if (localOp.IsError)
        {
            Debug.LogError("WebRtcVideoReceiver: SetLocalDescription answer failed: " + localOp.Error.message);
            yield break;
        }

        Debug.Log("WebRtcVideoReceiver: Local answer applied.");

        SdpSignal answerSignal = new SdpSignal
        {
            sdp = answer.sdp
        };

        string json = JsonUtility.ToJson(answerSignal);

        Debug.Log("WebRtcVideoReceiver: Sending answer. Payload length: " + json.Length);

        WebRtcSignalHub.Instance.SendSignal(from, "video.answer", json);

        Debug.Log("WebRtcVideoReceiver: WebRTC answer sent.");
    }

    private void HandleCandidate(string payload)
    {
        Debug.Log("WebRtcVideoReceiver: Candidate received. RemoteDescriptionSet = " + remoteDescriptionSet);

        if (!remoteDescriptionSet)
        {
            pendingCandidates.Enqueue(payload);
            Debug.Log("WebRtcVideoReceiver: Candidate queued. Queue count: " + pendingCandidates.Count);
            return;
        }

        AddRemoteCandidate(payload);
    }

    private void FlushPendingCandidates()
    {
        Debug.Log("WebRtcVideoReceiver: Flushing pending candidates. Count: " + pendingCandidates.Count);

        while (pendingCandidates.Count > 0)
        {
            AddRemoteCandidate(pendingCandidates.Dequeue());
        }
    }

    private void AddRemoteCandidate(string payload)
    {
        if (peerConnection == null)
        {
            Debug.LogWarning("WebRtcVideoReceiver: peerConnection is null when adding candidate.");
            return;
        }

        IceSignal signal = JsonUtility.FromJson<IceSignal>(payload);

        if (signal == null || string.IsNullOrEmpty(signal.candidate))
        {
            Debug.LogWarning("WebRtcVideoReceiver: Invalid candidate payload.");
            return;
        }

        Debug.Log("WebRtcVideoReceiver: Remote ICE candidate received: " + signal.candidate);
        LogCandidateType("WebRtcVideoReceiver remote", signal.candidate);

        RTCIceCandidateInit init = new RTCIceCandidateInit
        {
            candidate = signal.candidate,
            sdpMid = signal.sdpMid,
            sdpMLineIndex = signal.sdpMLineIndex >= 0 ? signal.sdpMLineIndex : null
        };

        peerConnection.AddIceCandidate(new RTCIceCandidate(init));

        Debug.Log("WebRtcVideoReceiver: Remote ICE candidate added.");
    }

    public void StopReceiving()
    {
        remoteDescriptionSet = false;
        pendingCandidates.Clear();

        if (videoDisplayScreen != null)
        {
            videoDisplayScreen.ClearTexture();
        }

        if (remoteVideoTrack != null)
        {
            remoteVideoTrack.Dispose();
            remoteVideoTrack = null;
        }

        if (peerConnection != null)
        {
            peerConnection.Close();
            peerConnection.Dispose();
            peerConnection = null;
        }

        Debug.Log("WebRtcVideoReceiver: Receiver stopped.");
    }

    private void OnDestroy()
    {
        if (WebRtcSignalHub.Instance != null)
        {
            WebRtcSignalHub.Instance.OnSignalReceived -= OnSignalReceived;
        }

        StopReceiving();

    }
}
