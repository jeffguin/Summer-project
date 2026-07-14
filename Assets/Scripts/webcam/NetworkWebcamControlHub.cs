using System;
using Fusion;
using UnityEngine;

public class NetworkWebcamControlHub : NetworkBehaviour
{
    private AudienceWebcamRuntime audienceRuntime;
    private PerformerWebcamControlPanel performerPanel;
    private WebRtcAudioEndpoint actorAudioEndpoint;

    public override void Spawned()
    {
        audienceRuntime = FindFirstObjectByType<AudienceWebcamRuntime>(FindObjectsInactive.Include);
        performerPanel = FindFirstObjectByType<PerformerWebcamControlPanel>(FindObjectsInactive.Include);
        actorAudioEndpoint = FindActorAudioEndpoint();

        Debug.Log("NetworkWebcamControlHub spawned. LocalPlayer: " + Runner.LocalPlayer);

        if (audienceRuntime != null)
        {
            Debug.Log("NetworkWebcamControlHub: Audience runtime found. Reporting camera list soon.");
            Invoke(nameof(ReportLocalAudienceCameraList), 1.0f);
        }

        if (performerPanel != null)
        {
            Debug.Log("NetworkWebcamControlHub: Performer panel found.");
        }

        if (actorAudioEndpoint != null)
        {
            Debug.Log("NetworkWebcamControlHub: Actor audio endpoint found.");
        }
    }

    private void ReportLocalAudienceCameraList()
    {
        if (audienceRuntime == null)
        {
            Debug.LogWarning("NetworkWebcamControlHub: audienceRuntime missing.");
            return;
        }

        string[] names = audienceRuntime.GetCameraNames();

        Debug.Log("NetworkWebcamControlHub: Reporting camera count: " + names.Length);

        string joinedNames = string.Join("\n", names);

        RPC_ReportCameraList(joinedNames);
    }

    public void RequestStartAudienceVideo(int cameraIndex)
    {
        Debug.Log("NetworkWebcamControlHub: RequestStartAudienceVideo " + cameraIndex);
        RPC_StartAudienceVideo(cameraIndex);
    }

    public void RequestStopAudienceVideo()
    {
        Debug.Log("NetworkWebcamControlHub: RequestStopAudienceVideo");
        RPC_StopAudienceVideo();
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_ReportCameraList(string joinedNames)
    {
        string[] names;

        if (string.IsNullOrEmpty(joinedNames))
            names = Array.Empty<string>();
        else
            names = joinedNames.Split('\n');

        Debug.Log("NetworkWebcamControlHub: Received camera list. Count: " + names.Length);

        PerformerWebcamControlPanel panel =
            FindFirstObjectByType<PerformerWebcamControlPanel>(FindObjectsInactive.Include);

        if (panel != null)
        {
            panel.SetCameraList(names);
            Debug.Log("NetworkWebcamControlHub: Performer dropdown updated.");
        }
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_StartAudienceVideo(int cameraIndex)
    {
        AudienceWebcamRuntime runtime =
            FindFirstObjectByType<AudienceWebcamRuntime>(FindObjectsInactive.Include);

        if (runtime == null)
        {
            Debug.Log("NetworkWebcamControlHub: This client has no AudienceWebcamRuntime. Ignore start command.");
            return;
        }

        Debug.Log("NetworkWebcamControlHub: Audience starts camera index " + cameraIndex);
        runtime.StartAudienceVideo(cameraIndex);
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_StopAudienceVideo()
    {
        AudienceWebcamRuntime runtime =
            FindFirstObjectByType<AudienceWebcamRuntime>(FindObjectsInactive.Include);

        if (runtime == null)
        {
            Debug.Log("NetworkWebcamControlHub: This client has no AudienceWebcamRuntime. Ignore stop command.");
            return;
        }

        Debug.Log("NetworkWebcamControlHub: Audience stops camera.");
        runtime.StopAudienceVideo();
    }

    // Audio control remains a small Fusion-backed control plane. The actual
    // SDP/ICE exchange and media are owned by WebRtcAudioEndpoint.
    public void RequestAudienceMicrophoneList()
    {
        WebRtcAudioEndpoint endpoint = GetActorAudioEndpoint();
        if (endpoint == null || !endpoint.RequestAudienceMicrophoneList())
            Debug.LogWarning("NetworkWebcamControlHub: Could not request the Audience microphone list.");
    }

    public void RequestSelectAudienceMicrophone(string deviceName)
    {
        WebRtcAudioEndpoint endpoint = GetActorAudioEndpoint();
        if (endpoint == null || !endpoint.SelectAudienceMicrophone(deviceName))
            Debug.LogWarning("NetworkWebcamControlHub: Could not select the Audience microphone.");
    }

    public void RequestStartAudienceAudio()
    {
        WebRtcAudioEndpoint endpoint = GetActorAudioEndpoint();
        if (endpoint == null)
        {
            Debug.LogWarning("NetworkWebcamControlHub: Actor audio endpoint is missing.");
            return;
        }

        endpoint.StartAudioSession();
    }

    public void RequestStopAudienceAudio()
    {
        WebRtcAudioEndpoint endpoint = GetActorAudioEndpoint();
        if (endpoint == null)
        {
            Debug.LogWarning("NetworkWebcamControlHub: Actor audio endpoint is missing.");
            return;
        }

        endpoint.StopAudioSession();
    }

    public WebRtcAudioEndpoint GetActorAudioEndpoint()
    {
        if (actorAudioEndpoint == null)
            actorAudioEndpoint = FindActorAudioEndpoint();

        return actorAudioEndpoint;
    }

    private static WebRtcAudioEndpoint FindActorAudioEndpoint()
    {
        WebRtcAudioEndpoint[] endpoints =
            FindObjectsByType<WebRtcAudioEndpoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (WebRtcAudioEndpoint endpoint in endpoints)
        {
            if (endpoint.Role == WebRtcAudioEndpoint.EndpointRole.Actor)
                return endpoint;
        }

        return null;
    }
}
