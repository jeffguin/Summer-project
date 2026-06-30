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

    public void StartAudienceVideo(int cameraIndex)
    {
        if (webcamManager == null || webRtcSender == null)
        {
            Debug.LogWarning("AudienceWebcamRuntime: Missing references.");
            return;
        }

        Debug.Log("AudienceWebcamRuntime: Start audience video. Camera index: " + cameraIndex);

        webcamManager.StartCameraByIndex(cameraIndex);
        webRtcSender.StartWebcamStream();
    }

    public void StopAudienceVideo()
    {
        if (webcamManager == null || webRtcSender == null)
        {
            Debug.LogWarning("AudienceWebcamRuntime: Missing references.");
            return;
        }

        Debug.Log("AudienceWebcamRuntime: Stop audience video.");

        webRtcSender.StopWebcamStream();
        webcamManager.StopWebcam();
    }
}