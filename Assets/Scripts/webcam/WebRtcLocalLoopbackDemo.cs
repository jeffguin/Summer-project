using System.Collections;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.UI;

public class WebRtcLocalLoopbackDemo : MonoBehaviour
{
    [Header("Webcam Source")]
    [SerializeField] private LocalWebcamManager localWebcamManager;

    [Header("Receiver Display")]
    [SerializeField] private VideoDisplayScreen receiverDisplayScreen;

    [Header("UI")]
    [SerializeField] private Button startWebRtcButton;
    [SerializeField] private Button stopWebRtcButton;

    private RTCPeerConnection senderPeer;
    private RTCPeerConnection receiverPeer;

    private VideoStreamTrack webcamVideoTrack;
    private Coroutine webRtcUpdateCoroutine;

    private bool isRunning = false;

    private void Start()
    {
        webRtcUpdateCoroutine = StartCoroutine(WebRTC.Update());

        if (startWebRtcButton != null)
        {
            startWebRtcButton.onClick.AddListener(StartLoopback);
        }

        if (stopWebRtcButton != null)
        {
            stopWebRtcButton.onClick.AddListener(StopLoopback);
        }
    }

    public void StartLoopback()
    {
        if (isRunning)
        {
            Debug.LogWarning("WebRTC loopback is already running.");
            return;
        }

        if (localWebcamManager == null)
        {
            Debug.LogError("WebRtcLocalLoopbackDemo: LocalWebcamManager is missing.");
            return;
        }

        WebCamTexture webcamTexture = localWebcamManager.GetCurrentWebcamTexture();

        if (webcamTexture == null || !webcamTexture.isPlaying)
        {
            Debug.LogError("Please start the webcam first.");
            return;
        }

        StartCoroutine(StartLoopbackRoutine(webcamTexture));
    }

    private IEnumerator StartLoopbackRoutine(WebCamTexture webcamTexture)
    {
        isRunning = true;

        RTCConfiguration config = default;

        senderPeer = new RTCPeerConnection(ref config);
        receiverPeer = new RTCPeerConnection(ref config);

        senderPeer.OnIceCandidate = candidate =>
        {
            if (candidate != null)
            {
                receiverPeer.AddIceCandidate(candidate);
            }
        };

        receiverPeer.OnIceCandidate = candidate =>
        {
            if (candidate != null)
            {
                senderPeer.AddIceCandidate(candidate);
            }
        };

        receiverPeer.OnTrack = e =>
        {
            VideoStreamTrack videoTrack = e.Track as VideoStreamTrack;

            if (videoTrack == null)
            {
                return;
            }

            videoTrack.OnVideoReceived += texture =>
            {
                if (receiverDisplayScreen != null)
                {
                    receiverDisplayScreen.SetTexture(texture);
                }

                Debug.Log("WebRTC receiver got video texture.");
            };
        };

        webcamVideoTrack = new VideoStreamTrack(webcamTexture);
        senderPeer.AddTrack(webcamVideoTrack);

        RTCSessionDescriptionAsyncOperation offerOp = senderPeer.CreateOffer();
        yield return offerOp;

        if (offerOp.IsError)
        {
            Debug.LogError("CreateOffer failed: " + offerOp.Error.message);
            StopLoopback();
            yield break;
        }

        RTCSessionDescription offerDesc = offerOp.Desc;

        RTCSetSessionDescriptionAsyncOperation senderLocalOp =
            senderPeer.SetLocalDescription(ref offerDesc);

        yield return senderLocalOp;

        if (senderLocalOp.IsError)
        {
            Debug.LogError("SetLocalDescription offer failed: " + senderLocalOp.Error.message);
            StopLoopback();
            yield break;
        }

        RTCSetSessionDescriptionAsyncOperation receiverRemoteOp =
            receiverPeer.SetRemoteDescription(ref offerDesc);

        yield return receiverRemoteOp;

        if (receiverRemoteOp.IsError)
        {
            Debug.LogError("SetRemoteDescription offer failed: " + receiverRemoteOp.Error.message);
            StopLoopback();
            yield break;
        }

        RTCSessionDescriptionAsyncOperation answerOp = receiverPeer.CreateAnswer();
        yield return answerOp;

        if (answerOp.IsError)
        {
            Debug.LogError("CreateAnswer failed: " + answerOp.Error.message);
            StopLoopback();
            yield break;
        }

        RTCSessionDescription answerDesc = answerOp.Desc;

        RTCSetSessionDescriptionAsyncOperation receiverLocalOp =
            receiverPeer.SetLocalDescription(ref answerDesc);

        yield return receiverLocalOp;

        if (receiverLocalOp.IsError)
        {
            Debug.LogError("SetLocalDescription answer failed: " + receiverLocalOp.Error.message);
            StopLoopback();
            yield break;
        }

        RTCSetSessionDescriptionAsyncOperation senderRemoteOp =
            senderPeer.SetRemoteDescription(ref answerDesc);

        yield return senderRemoteOp;

        if (senderRemoteOp.IsError)
        {
            Debug.LogError("SetRemoteDescription answer failed: " + senderRemoteOp.Error.message);
            StopLoopback();
            yield break;
        }

        Debug.Log("WebRTC local loopback started.");
    }

    public void StopLoopback()
    {
        isRunning = false;

        if (webcamVideoTrack != null)
        {
            webcamVideoTrack.Dispose();
            webcamVideoTrack = null;
        }

        if (senderPeer != null)
        {
            senderPeer.Close();
            senderPeer.Dispose();
            senderPeer = null;
        }

        if (receiverPeer != null)
        {
            receiverPeer.Close();
            receiverPeer.Dispose();
            receiverPeer = null;
        }

        if (receiverDisplayScreen != null)
        {
            receiverDisplayScreen.ClearTexture();
        }

        Debug.Log("WebRTC local loopback stopped.");
    }

    private void OnDestroy()
    {
        StopLoopback();

        if (webRtcUpdateCoroutine != null)
        {
            StopCoroutine(webRtcUpdateCoroutine);
            webRtcUpdateCoroutine = null;
        }
    }
}