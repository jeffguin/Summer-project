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

    // =========================
    // Video Control
    // =========================

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
            Debug.Log("NetworkWebcamControlHub: Performer camera dropdown updated.");
        }
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_StartAudienceVideo(int cameraIndex)
    {
        AudienceWebcamRuntime runtime = FindObjectOfType<AudienceWebcamRuntime>(true);

        if (runtime == null)
        {
            Debug.Log("NetworkWebcamControlHub: This client has no AudienceWebcamRuntime. Ignore video start command.");
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
            Debug.Log("NetworkWebcamControlHub: This client has no AudienceWebcamRuntime. Ignore video stop command.");
            return;
        }

        Debug.Log("NetworkWebcamControlHub: Audience stops camera.");
        runtime.StopAudienceVideo();
    }

    // =========================
    // Audience Microphone Device List
    // =========================

    public void RequestAudienceMicrophoneList()
    {
        Debug.Log("NetworkWebcamControlHub: RequestAudienceMicrophoneList");
        RPC_RequestAudienceMicrophoneList();
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_RequestAudienceMicrophoneList()
    {
        WebRtcWebcamSender sender = FindObjectOfType<WebRtcWebcamSender>(true);

        if (sender == null)
        {
            Debug.Log("NetworkWebcamControlHub: This client has no WebRtcWebcamSender. Ignore microphone list request.");
            return;
        }

        string[] devices = sender.GetLocalMicrophoneDevices();
        string selectedDevice = sender.GetMicrophoneDeviceName();

        string joinedDevices = "";

        if (devices != null && devices.Length > 0)
        {
            joinedDevices = string.Join("\n", devices);
        }

        Debug.Log(
            "NetworkWebcamControlHub: Audience reporting microphone list. " +
            "Count = " + (devices != null ? devices.Length : 0) +
            ", Selected = " + (string.IsNullOrEmpty(selectedDevice) ? "Default" : selectedDevice)
        );

        RPC_ReportAudienceMicrophoneList(joinedDevices, selectedDevice);
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_ReportAudienceMicrophoneList(string joinedDevices, string selectedDevice)
    {
        string[] devices;

        if (string.IsNullOrEmpty(joinedDevices))
            devices = Array.Empty<string>();
        else
            devices = joinedDevices.Split('\n');

        Debug.Log("NetworkWebcamControlHub: Received audience microphone list. Count: " + devices.Length);

        PerformerWebcamControlPanel panel = FindObjectOfType<PerformerWebcamControlPanel>(true);

        if (panel != null)
        {
            panel.SetAudienceMicrophoneList(devices, selectedDevice);
            Debug.Log("NetworkWebcamControlHub: Performer audience microphone dropdown updated.");
        }
    }

    public void RequestSelectAudienceMicrophone(string deviceName)
    {
        Debug.Log(
            "NetworkWebcamControlHub: RequestSelectAudienceMicrophone " +
            (string.IsNullOrEmpty(deviceName) ? "Default" : deviceName)
        );

        RPC_SelectAudienceMicrophone(deviceName);
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_SelectAudienceMicrophone(string deviceName)
    {
        WebRtcWebcamSender sender = FindObjectOfType<WebRtcWebcamSender>(true);

        if (sender == null)
        {
            Debug.Log("NetworkWebcamControlHub: This client has no WebRtcWebcamSender. Ignore microphone selection.");
            return;
        }

        Debug.Log(
            "NetworkWebcamControlHub: Audience microphone selected: " +
            (string.IsNullOrEmpty(deviceName) ? "Default" : deviceName)
        );

        sender.SetMicrophoneDeviceName(deviceName);
    }

    // =========================
    // Audio Control
    // =========================

    public void RequestStartAudienceAudio()
    {
        Debug.Log("NetworkWebcamControlHub: RequestStartAudienceAudio");
        RPC_StartAudienceAudio();
    }

    public void RequestStopAudienceAudio()
    {
        Debug.Log("NetworkWebcamControlHub: RequestStopAudienceAudio");
        RPC_StopAudienceAudio();
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_StartAudienceAudio()
    {
        WebRtcWebcamSender sender = FindObjectOfType<WebRtcWebcamSender>(true);

        if (sender == null)
        {
            Debug.Log("NetworkWebcamControlHub: This client has no WebRtcWebcamSender. Ignore audio start command.");
            return;
        }

        Debug.Log("NetworkWebcamControlHub: Audience starts audio.");

        sender.SendMessage("StartAudioStream", SendMessageOptions.DontRequireReceiver);
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_StopAudienceAudio()
    {
        WebRtcWebcamSender sender = FindObjectOfType<WebRtcWebcamSender>(true);

        if (sender == null)
        {
            Debug.Log("NetworkWebcamControlHub: This client has no WebRtcWebcamSender. Ignore audio stop command.");
            return;
        }

        Debug.Log("NetworkWebcamControlHub: Audience stops audio.");

        sender.SendMessage("StopAudioStream", SendMessageOptions.DontRequireReceiver);
    }
}