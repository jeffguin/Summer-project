using System;
using Fusion;
using UnityEngine;

public class AudienceWebcamRuntime : MonoBehaviour
{
    [Header("Audience Local Components")]
    [SerializeField] private LocalWebcamManager webcamManager;
    [SerializeField] private WebRtcWebcamSender webRtcSender;

    public string[] GetCameraNames()
    {
        if (webcamManager == null)
        {
            Debug.LogWarning("AudienceWebcamRuntime: webcamManager is missing.");
            return new string[0];
        }

        return webcamManager.GetCameraNames();
    }

    public void StartAudienceVideo(string sessionId, PlayerRef actorPlayer)
    {
        if (webcamManager == null || webRtcSender == null)
        {
            Debug.LogWarning("AudienceWebcamRuntime: Missing references.");
            return;
        }

        Debug.Log(
            "AudienceWebcamRuntime: Start all Audience cameras. Session: " + sessionId +
            ", Actor: " + actorPlayer
        );

        webRtcSender.StartWebcamStream(sessionId, actorPlayer);
    }

    // Compatibility overload for older local callers.
    public void StartAudienceVideo(string sessionId, PlayerRef actorPlayer, int cameraIndex)
    {
        StartAudienceVideo(sessionId, actorPlayer);
    }

    // Compatibility entry point for optional local/test callers. Network control
    // should use the overload with an Actor-created session id and explicit target.
    public void StartAudienceVideo(int cameraIndex)
    {
        if (WebRtcSignalHub.Instance == null)
        {
            Debug.LogWarning("AudienceWebcamRuntime: SignalHub is unavailable.");
            return;
        }

        PlayerRef actorPlayer = WebRtcSignalHub.Instance.GetOtherPlayer();
        if (actorPlayer == PlayerRef.None)
        {
            Debug.LogWarning("AudienceWebcamRuntime: Actor player is unavailable.");
            return;
        }

        StartAudienceVideo(Guid.NewGuid().ToString("N"), actorPlayer);
    }

    public void StopAudienceVideo()
    {
        if (webcamManager == null || webRtcSender == null)
        {
            Debug.LogWarning("AudienceWebcamRuntime: Missing references.");
            return;
        }

        Debug.Log("AudienceWebcamRuntime: Stop audience video.");

        webRtcSender.RequestStopWebcamStream();
    }

    public void ForceStopAudienceVideo()
    {
        if (webRtcSender != null)
            webRtcSender.ForceStopWebcamStream();

        if (webcamManager != null)
            webcamManager.StopWebcam();
    }
}
