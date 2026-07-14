using System;
using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PerformerWebcamControlPanel : MonoBehaviour
{
    [Header("Video UI")]
    [SerializeField] private TMP_Dropdown cameraDropdown;
    [SerializeField] private Button startButton;
    [SerializeField] private Button stopButton;

    [Header("Audio UI")]
    [SerializeField] private TMP_Dropdown actorMicDropdown;
    [SerializeField] private TMP_Dropdown audienceMicDropdown;

    [Tooltip("Optional. If assigned, clicking this will refresh actor/audience microphone lists.")]
    [SerializeField] private Button refreshAudioDevicesButton;

    [Header("Actor Audio Receiver")]
    [Tooltip("The WebRtcVideoReceiver on the Actor / Quest side. Used to set the Actor microphone device.")]
    [SerializeField] private WebRtcVideoReceiver actorReceiver;

    [Header("Runtime State")]
    [SerializeField] private int selectedCameraIndex = 0;
    [SerializeField] private int selectedActorMicIndex = 0;
    [SerializeField] private int selectedAudienceMicIndex = 0;

    private NetworkWebcamControlHub controlHub;

    private Coroutine requestAudienceMicListCoroutine;
    private bool signalHubSubscribed = false;

    private readonly List<string> actorMicDevices = new List<string>();
    private readonly List<string> audienceMicDevices = new List<string>();

    [Serializable]
    private class MicrophoneDeviceListSignal
    {
        public string[] devices;
        public string selectedDevice;
    }

    [Serializable]
    private class MicrophoneDeviceSelectSignal
    {
        public string deviceName;
    }

    private void Awake()
    {
        if (cameraDropdown != null)
        {
            cameraDropdown.onValueChanged.AddListener(OnCameraSelected);
        }

        if (startButton != null)
        {
            startButton.onClick.AddListener(OnStartClicked);
        }

        if (stopButton != null)
        {
            stopButton.onClick.AddListener(OnStopClicked);
        }

        if (actorMicDropdown != null)
        {
            actorMicDropdown.onValueChanged.AddListener(OnActorMicrophoneSelected);
        }

        if (audienceMicDropdown != null)
        {
            audienceMicDropdown.onValueChanged.AddListener(OnAudienceMicrophoneSelected);
        }

        if (refreshAudioDevicesButton != null)
        {
            refreshAudioDevicesButton.onClick.AddListener(RefreshAudioDeviceLists);
        }

        SetWaitingState();
        SetAudioWaitingState();
    }

    private void Start()
    {
        if (actorReceiver == null)
        {
            actorReceiver =
                FindFirstObjectByType<WebRtcVideoReceiver>(FindObjectsInactive.Include);

            if (actorReceiver != null)
            {
                Debug.Log("PerformerWebcamControlPanel: Auto-found WebRtcVideoReceiver on " + actorReceiver.gameObject.name);
            }
            else
            {
                Debug.LogWarning("PerformerWebcamControlPanel: WebRtcVideoReceiver not found. Actor mic selection will not be applied.");
            }
        }

        RefreshActorMicrophoneList();
        StartCoroutine(WaitForSignalHub());
    }

    private void OnEnable()
    {
        TryFindControlHub();

        RefreshActorMicrophoneList();

        if (WebRtcSignalHub.Instance != null)
        {
            SubscribeToSignalHub();
            RequestAudienceMicrophoneList();
        }
    }

    private void OnDisable()
    {
        if (requestAudienceMicListCoroutine != null)
        {
            StopCoroutine(requestAudienceMicListCoroutine);
            requestAudienceMicListCoroutine = null;
        }
    }

    private void OnDestroy()
    {
        if (cameraDropdown != null)
        {
            cameraDropdown.onValueChanged.RemoveListener(OnCameraSelected);
        }

        if (startButton != null)
        {
            startButton.onClick.RemoveListener(OnStartClicked);
        }

        if (stopButton != null)
        {
            stopButton.onClick.RemoveListener(OnStopClicked);
        }

        if (actorMicDropdown != null)
        {
            actorMicDropdown.onValueChanged.RemoveListener(OnActorMicrophoneSelected);
        }

        if (audienceMicDropdown != null)
        {
            audienceMicDropdown.onValueChanged.RemoveListener(OnAudienceMicrophoneSelected);
        }

        if (refreshAudioDevicesButton != null)
        {
            refreshAudioDevicesButton.onClick.RemoveListener(RefreshAudioDeviceLists);
        }

        if (WebRtcSignalHub.Instance != null)
        {
            WebRtcSignalHub.Instance.OnSignalReceived -= OnSignalReceived;
        }

        signalHubSubscribed = false;
    }

    private System.Collections.IEnumerator WaitForSignalHub()
    {
        while (WebRtcSignalHub.Instance == null)
        {
            yield return null;
        }

        SubscribeToSignalHub();

        Debug.Log("PerformerWebcamControlPanel: Connected to WebRtcSignalHub for audio device control.");

        RequestAudienceMicrophoneList();
    }

    private void SubscribeToSignalHub()
    {
        if (WebRtcSignalHub.Instance == null)
            return;

        WebRtcSignalHub.Instance.OnSignalReceived -= OnSignalReceived;
        WebRtcSignalHub.Instance.OnSignalReceived += OnSignalReceived;

        signalHubSubscribed = true;

        Debug.Log("PerformerWebcamControlPanel: Subscribed to WebRtcSignalHub.");
    }

    private void TryFindControlHub()
    {
        if (controlHub != null)
            return;

        controlHub = FindFirstObjectByType<NetworkWebcamControlHub>();

        if (controlHub == null)
        {
            Debug.LogWarning("PerformerWebcamControlPanel: NetworkWebcamControlHub not found yet.");
            return;
        }

        Debug.Log("PerformerWebcamControlPanel: NetworkWebcamControlHub found.");
    }

    // =========================
    // Video Camera UI
    // =========================

    public void SetCameraList(string[] cameraNames)
    {
        if (cameraDropdown == null)
        {
            Debug.LogError("PerformerWebcamControlPanel: cameraDropdown is NULL. Cannot display audience camera list.");
            return;
        }

        cameraDropdown.ClearOptions();

        if (cameraNames == null || cameraNames.Length == 0)
        {
            cameraDropdown.AddOptions(new List<string>
            {
                "No audience camera found"
            });

            selectedCameraIndex = 0;
            cameraDropdown.value = 0;
            cameraDropdown.RefreshShownValue();

            if (startButton != null)
                startButton.interactable = false;

            return;
        }

        cameraDropdown.AddOptions(new List<string>(cameraNames));

        selectedCameraIndex = 0;
        cameraDropdown.value = 0;
        cameraDropdown.RefreshShownValue();

        if (startButton != null)
            startButton.interactable = true;

        Debug.Log("PerformerWebcamControlPanel: Audience camera list updated. Count = " + cameraNames.Length);
    }

    private void SetWaitingState()
    {
        if (cameraDropdown != null)
        {
            cameraDropdown.ClearOptions();
            cameraDropdown.AddOptions(new List<string>
            {
                "Waiting for audience camera list..."
            });
            cameraDropdown.RefreshShownValue();
        }

        if (startButton != null)
            startButton.interactable = false;
    }

    public void OnCameraSelected(int index)
    {
        selectedCameraIndex = index;

        Debug.Log("PerformerWebcamControlPanel: Camera selected. Index = " + selectedCameraIndex);
    }

    public void OnStartClicked()
    {
        TryFindControlHub();

        if (controlHub == null)
        {
            Debug.LogWarning("PerformerWebcamControlPanel: Cannot start. NetworkWebcamControlHub is missing.");
            return;
        }

        Debug.Log("Performer requested Start Audience Video. Camera index: " + selectedCameraIndex);

        controlHub.RequestStartAudienceVideo(selectedCameraIndex);
    }

    public void OnStopClicked()
    {
        TryFindControlHub();

        if (controlHub == null)
        {
            Debug.LogWarning("PerformerWebcamControlPanel: Cannot stop. NetworkWebcamControlHub is missing.");
            return;
        }

        Debug.Log("Performer requested Stop Audience Video.");

        controlHub.RequestStopAudienceVideo();
    }

    // =========================
    // Audio Device UI
    // =========================

    private void SetAudioWaitingState()
    {
        if (actorMicDropdown != null)
        {
            actorMicDropdown.ClearOptions();
            actorMicDropdown.AddOptions(new List<string>
            {
                "Loading actor microphones..."
            });
            actorMicDropdown.RefreshShownValue();
            actorMicDropdown.interactable = false;
        }

        if (audienceMicDropdown != null)
        {
            audienceMicDropdown.ClearOptions();
            audienceMicDropdown.AddOptions(new List<string>
            {
                "Waiting for audience microphones..."
            });
            audienceMicDropdown.RefreshShownValue();
            audienceMicDropdown.interactable = false;
        }
    }

    public void RefreshAudioDeviceLists()
    {
        Debug.Log("PerformerWebcamControlPanel: RefreshAudioDeviceLists clicked.");

        RefreshActorMicrophoneList();
        RequestAudienceMicrophoneList();
    }

    private void RefreshActorMicrophoneList()
    {
        actorMicDevices.Clear();

        // Empty string means default microphone.
        actorMicDevices.Add("");

        string[] devices = Microphone.devices;

        if (devices != null && devices.Length > 0)
        {
            actorMicDevices.AddRange(devices);
        }

        if (actorMicDropdown == null)
        {
            Debug.LogError("PerformerWebcamControlPanel: actorMicDropdown is NULL. Cannot display actor microphone list.");
            return;
        }

        actorMicDropdown.ClearOptions();

        List<string> options = new List<string>();

        for (int i = 0; i < actorMicDevices.Count; i++)
        {
            string device = actorMicDevices[i];

            if (string.IsNullOrEmpty(device))
            {
                options.Add("Default Actor Microphone");
            }
            else
            {
                options.Add(device);
            }
        }

        actorMicDropdown.AddOptions(options);

        selectedActorMicIndex = 0;
        actorMicDropdown.value = 0;
        actorMicDropdown.RefreshShownValue();
        actorMicDropdown.interactable = true;

        ApplyActorMicrophoneSelection(0);

        Debug.Log("PerformerWebcamControlPanel: Actor microphone list refreshed. Count = " + actorMicDevices.Count);
    }

    private void RequestAudienceMicrophoneList()
    {
        if (requestAudienceMicListCoroutine != null)
        {
            StopCoroutine(requestAudienceMicListCoroutine);
            requestAudienceMicListCoroutine = null;
        }

        requestAudienceMicListCoroutine = StartCoroutine(RequestAudienceMicrophoneListRoutine());
    }

    private System.Collections.IEnumerator RequestAudienceMicrophoneListRoutine()
    {
        int retryCount = 0;
        int maxRetryCount = 30;

        while (retryCount < maxRetryCount)
        {
            if (WebRtcSignalHub.Instance == null)
            {
                Debug.LogWarning(
                    "PerformerWebcamControlPanel: WebRtcSignalHub is missing. " +
                    "Retry audience microphone list request."
                );

                retryCount++;
                yield return new WaitForSeconds(1f);
                continue;
            }

            if (!signalHubSubscribed)
            {
                SubscribeToSignalHub();
            }

            PlayerRef target = WebRtcSignalHub.Instance.GetOtherPlayer();

            if (target == PlayerRef.None)
            {
                Debug.LogWarning(
                    "PerformerWebcamControlPanel: No audience player found. " +
                    "Retry audience microphone list request."
                );

                retryCount++;
                yield return new WaitForSeconds(1f);
                continue;
            }

            Debug.Log("PerformerWebcamControlPanel: Requesting audience microphone list from " + target);

            WebRtcSignalHub.Instance.SendSignal(
                target,
                "audience_mic_list_request",
                "{}"
            );

            requestAudienceMicListCoroutine = null;
            yield break;
        }

        Debug.LogWarning("PerformerWebcamControlPanel: Failed to request audience microphone list after retries.");

        requestAudienceMicListCoroutine = null;
    }

    private void OnSignalReceived(PlayerRef from, string type, string payload)
    {
        Debug.Log(
            "PerformerWebcamControlPanel: Signal received. " +
            "Type = " + type +
            ", From = " + from +
            ", PayloadLength = " + (payload != null ? payload.Length : 0)
        );

        if (type == "audience_mic_list")
        {
            HandleAudienceMicrophoneList(payload);
        }
    }

    private void HandleAudienceMicrophoneList(string payload)
    {
        Debug.Log("PerformerWebcamControlPanel: Handling audience microphone list. Payload = " + payload);

        MicrophoneDeviceListSignal signal =
            JsonUtility.FromJson<MicrophoneDeviceListSignal>(payload);

        if (signal == null)
        {
            Debug.LogWarning("PerformerWebcamControlPanel: Invalid audience microphone list payload.");
            return;
        }

        int count = signal.devices != null ? signal.devices.Length : 0;

        Debug.Log(
            "PerformerWebcamControlPanel: Parsed audience microphone list. " +
            "DeviceCount = " + count +
            ", Selected = " + (string.IsNullOrEmpty(signal.selectedDevice) ? "Default" : signal.selectedDevice)
        );

        SetAudienceMicrophoneList(signal.devices, signal.selectedDevice);
    }

    public void SetAudienceMicrophoneList(string[] devices, string selectedDevice)
    {
        audienceMicDevices.Clear();

        // Empty string means default microphone.
        audienceMicDevices.Add("");

        if (devices != null && devices.Length > 0)
        {
            audienceMicDevices.AddRange(devices);
        }

        if (audienceMicDropdown == null)
        {
            Debug.LogError("PerformerWebcamControlPanel: audienceMicDropdown is NULL. Cannot display audience microphone list.");
            return;
        }

        audienceMicDropdown.ClearOptions();

        List<string> options = new List<string>();

        for (int i = 0; i < audienceMicDevices.Count; i++)
        {
            string device = audienceMicDevices[i];

            if (string.IsNullOrEmpty(device))
            {
                options.Add("Default Audience Microphone");
            }
            else
            {
                options.Add(device);
            }
        }

        audienceMicDropdown.AddOptions(options);

        int selectedIndex = 0;

        for (int i = 0; i < audienceMicDevices.Count; i++)
        {
            if (audienceMicDevices[i] == selectedDevice)
            {
                selectedIndex = i;
                break;
            }
        }

        selectedAudienceMicIndex = selectedIndex;
        audienceMicDropdown.value = selectedIndex;
        audienceMicDropdown.RefreshShownValue();
        audienceMicDropdown.interactable = true;

        Debug.Log("PerformerWebcamControlPanel: Audience microphone list updated. Count = " + audienceMicDevices.Count);
    }

    public void OnActorMicrophoneSelected(int index)
    {
        ApplyActorMicrophoneSelection(index);
    }

    private void ApplyActorMicrophoneSelection(int index)
    {
        if (index < 0 || index >= actorMicDevices.Count)
            return;

        selectedActorMicIndex = index;

        string selectedDevice = actorMicDevices[index];

        Debug.Log(
            "PerformerWebcamControlPanel: Actor microphone selected: " +
            (string.IsNullOrEmpty(selectedDevice) ? "Default" : selectedDevice)
        );

        if (actorReceiver != null)
        {
            actorReceiver.SetMicrophoneDeviceName(selectedDevice);
        }
        else
        {
            Debug.LogWarning("PerformerWebcamControlPanel: actorReceiver is null. Actor microphone selection was not applied.");
        }
    }

    public void OnAudienceMicrophoneSelected(int index)
    {
        if (index < 0 || index >= audienceMicDevices.Count)
            return;

        selectedAudienceMicIndex = index;

        if (WebRtcSignalHub.Instance == null)
        {
            Debug.LogWarning("PerformerWebcamControlPanel: Cannot send audience microphone selection. WebRtcSignalHub is missing.");
            return;
        }

        PlayerRef target = WebRtcSignalHub.Instance.GetOtherPlayer();

        if (target == PlayerRef.None)
        {
            Debug.LogWarning("PerformerWebcamControlPanel: Cannot send audience microphone selection. No audience player found.");
            return;
        }

        string selectedDevice = audienceMicDevices[index];

        MicrophoneDeviceSelectSignal signal = new MicrophoneDeviceSelectSignal
        {
            deviceName = selectedDevice
        };

        string json = JsonUtility.ToJson(signal);

        Debug.Log(
            "PerformerWebcamControlPanel: Sending audience microphone selection: " +
            (string.IsNullOrEmpty(selectedDevice) ? "Default" : selectedDevice)
        );

        WebRtcSignalHub.Instance.SendSignal(
            target,
            "audience_mic_select",
            json
        );
    }
}