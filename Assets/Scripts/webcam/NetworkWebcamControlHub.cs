using System;
using Fusion;
using UnityEngine;

public class NetworkWebcamControlHub : NetworkBehaviour
{
    private AudienceWebcamRuntime audienceRuntime;
    private PerformerWebcamControlPanel performerPanel;

    public override void Spawned()
    {
        audienceRuntime = FindObjectOfType<AudienceWebcamRuntime>(true);
        performerPanel = FindObjectOfType<PerformerWebcamControlPanel>(true);

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

        PerformerWebcamControlPanel panel = FindObjectOfType<PerformerWebcamControlPanel>(true);

        if (panel != null)
        {
            panel.SetCameraList(names);
            Debug.Log("NetworkWebcamControlHub: Performer dropdown updated.");
        }
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_StartAudienceVideo(int cameraIndex)
    {
        AudienceWebcamRuntime runtime = FindObjectOfType<AudienceWebcamRuntime>(true);

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
        AudienceWebcamRuntime runtime = FindObjectOfType<AudienceWebcamRuntime>(true);

        if (runtime == null)
        {
            Debug.Log("NetworkWebcamControlHub: This client has no AudienceWebcamRuntime. Ignore stop command.");
            return;
        }

        Debug.Log("NetworkWebcamControlHub: Audience stops camera.");
        runtime.StopAudienceVideo();
    }
}