using System;
using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

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

    [Header("Audio Control Buttons")]
    [SerializeField] private Button startAudioButton;
    [SerializeField] private Button stopAudioButton;

    [Header("Actor Audio Receiver")]
    [Tooltip("The WebRtcVideoReceiver on the Actor / Quest side. Used to set the Actor microphone device.")]
    [SerializeField] private WebRtcVideoReceiver actorReceiver;

    [Header("Runtime State")]
    [SerializeField] private int selectedCameraIndex = 0;
    [SerializeField] private int selectedActorMicIndex = 0;
    [SerializeField] private int selectedAudienceMicIndex = 0;

    private NetworkWebcamControlHub controlHub;

    private Coroutine requestAudienceMicListCoroutine;
    private Coroutine initializeActorMicListCoroutine;
    private WebRtcSignalHub subscribedSignalHub;
    private bool audienceMicListReceived = false;
    private bool actorMicrophoneAvailable = false;
    private bool audienceMicrophoneAvailable = false;
    private bool audioStartRequested = false;

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

        if (startAudioButton != null)
        {
            startAudioButton.onClick.AddListener(OnStartAudioClicked);
        }

        if (stopAudioButton != null)
        {
            stopAudioButton.onClick.AddListener(OnStopAudioClicked);
        }

        SetWaitingState();
        SetAudioWaitingState();
    }

    private void Start()
    {
        if (actorReceiver == null)
        {
            actorReceiver = FindObjectOfType<WebRtcVideoReceiver>(true);

            if (actorReceiver != null)
            {
                Debug.Log("PerformerWebcamControlPanel: Auto-found WebRtcVideoReceiver on " + actorReceiver.gameObject.name);
            }
            else
            {
                Debug.LogWarning("PerformerWebcamControlPanel: WebRtcVideoReceiver not found. Actor mic selection will not be applied.");
            }
        }

        initializeActorMicListCoroutine = StartCoroutine(InitializeActorMicrophoneList());
        StartCoroutine(WaitForSignalHub());
    }

    private void OnEnable()
    {
        TryFindControlHub();

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

        if (initializeActorMicListCoroutine != null)
        {
            StopCoroutine(initializeActorMicListCoroutine);
            initializeActorMicListCoroutine = null;
        }

        UnsubscribeFromSignalHub();
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

        if (startAudioButton != null)
        {
            startAudioButton.onClick.RemoveListener(OnStartAudioClicked);
        }

        if (stopAudioButton != null)
        {
            stopAudioButton.onClick.RemoveListener(OnStopAudioClicked);
        }

        UnsubscribeFromSignalHub();
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

        if (subscribedSignalHub == WebRtcSignalHub.Instance)
            return;

        UnsubscribeFromSignalHub();

        subscribedSignalHub = WebRtcSignalHub.Instance;
        subscribedSignalHub.OnSignalReceived += OnSignalReceived;

        Debug.Log("PerformerWebcamControlPanel: Subscribed to WebRtcSignalHub.");
    }

    private void UnsubscribeFromSignalHub()
    {
        if (subscribedSignalHub == null)
            return;

        subscribedSignalHub.OnSignalReceived -= OnSignalReceived;
        subscribedSignalHub = null;
    }

    private void TryFindControlHub()
    {
        if (controlHub != null)
            return;

        controlHub = FindObjectOfType<NetworkWebcamControlHub>();

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
            Debug.LogWarning("PerformerWebcamControlPanel: Cannot start video. NetworkWebcamControlHub is missing.");
            return;
        }

        Debug.Log("PerformerWebcamControlPanel: Performer requested Start Audience Video. Camera index = " + selectedCameraIndex);

        controlHub.RequestStartAudienceVideo(selectedCameraIndex);
    }

    public void OnStopClicked()
    {
        TryFindControlHub();

        if (controlHub == null)
        {
            Debug.LogWarning("PerformerWebcamControlPanel: Cannot stop video. NetworkWebcamControlHub is missing.");
            return;
        }

        Debug.Log("PerformerWebcamControlPanel: Performer requested Stop Audience Video.");

        controlHub.RequestStopAudienceVideo();
    }

    // =========================
    // Audio Control UI
    // =========================

    public void OnStartAudioClicked()
    {
        if (!TrySendSignalToAudience("audio_start_request", "{}"))
        {
            Debug.LogWarning("PerformerWebcamControlPanel: Cannot start audio because the audience is not connected yet.");
            RequestAudienceMicrophoneList();
            return;
        }

        audioStartRequested = true;
        UpdateAudioButtonState();

        Debug.Log("PerformerWebcamControlPanel: Audio start request sent to Audience.");
    }

    public void OnStopAudioClicked()
    {
        if (!TrySendSignalToAudience("audio_stop_request", "{}"))
        {
            Debug.LogWarning("PerformerWebcamControlPanel: Cannot stop audio because the audience is not connected.");
            return;
        }

        audioStartRequested = false;
        UpdateAudioButtonState();

        Debug.Log("PerformerWebcamControlPanel: Audio stop request sent to Audience.");
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

        audienceMicListReceived = false;
        actorMicrophoneAvailable = false;
        audienceMicrophoneAvailable = false;
        audioStartRequested = false;
        UpdateAudioButtonState();
    }

    public void RefreshAudioDeviceLists()
    {
        Debug.Log("PerformerWebcamControlPanel: RefreshAudioDeviceLists clicked.");

        if (initializeActorMicListCoroutine != null)
        {
            StopCoroutine(initializeActorMicListCoroutine);
        }

        initializeActorMicListCoroutine = StartCoroutine(InitializeActorMicrophoneList());
        audienceMicListReceived = false;
        audienceMicrophoneAvailable = false;

        if (audienceMicDropdown != null)
        {
            audienceMicDropdown.ClearOptions();
            audienceMicDropdown.AddOptions(new List<string>
            {
                "Waiting for audience microphones..."
            });
            audienceMicDropdown.RefreshShownValue();
        }

        UpdateAudioButtonState();
        RequestAudienceMicrophoneList();
    }

    private System.Collections.IEnumerator InitializeActorMicrophoneList()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Debug.Log("PerformerWebcamControlPanel: Requesting Actor microphone permission before listing devices.");
            Permission.RequestUserPermission(Permission.Microphone);

            float timeout = Time.realtimeSinceStartup + 30f;
            while (!Permission.HasUserAuthorizedPermission(Permission.Microphone) &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Debug.LogError("PerformerWebcamControlPanel: Actor microphone permission was not granted.");
            }
        }
#else
        yield return null;
#endif

        RefreshActorMicrophoneList();
        initializeActorMicListCoroutine = null;
    }

    private void RefreshActorMicrophoneList()
    {
        actorMicDevices.Clear();

        // Empty string means default microphone.
        actorMicDevices.Add("");

        string[] devices = Microphone.devices;

        actorMicrophoneAvailable = devices != null && devices.Length > 0;

        if (actorMicrophoneAvailable)
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

        if (!actorMicrophoneAvailable)
        {
            options[0] = "No actor microphone found";
        }

        actorMicDropdown.AddOptions(options);

        string currentDevice = actorReceiver != null
            ? actorReceiver.GetMicrophoneDeviceName()
            : "";

        selectedActorMicIndex = actorMicDevices.IndexOf(currentDevice);
        if (selectedActorMicIndex < 0)
            selectedActorMicIndex = 0;

        actorMicDropdown.SetValueWithoutNotify(selectedActorMicIndex);
        actorMicDropdown.RefreshShownValue();
        actorMicDropdown.interactable = actorMicrophoneAvailable && !audioStartRequested;

        if (actorMicrophoneAvailable)
        {
            ApplyActorMicrophoneSelection(selectedActorMicIndex);
        }

        UpdateAudioButtonState();

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
        audienceMicListReceived = false;
        int retryCount = 0;

        while (!audienceMicListReceived)
        {
            if (WebRtcSignalHub.Instance == null)
            {
                Debug.LogWarning(
                    "PerformerWebcamControlPanel: WebRtcSignalHub is not ready. " +
                    "Retry audience microphone list request."
                );
            }
            else
            {
                SubscribeToSignalHub();

                if (TrySendSignalToAudience("audience_mic_list_request", "{}") &&
                    (retryCount == 0 || retryCount % 5 == 0))
                {
                    Debug.Log("PerformerWebcamControlPanel: Audience microphone list request sent. Attempt " + (retryCount + 1));
                }
            }

            retryCount++;
            yield return new WaitForSecondsRealtime(1f);
        }

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
        audienceMicListReceived = true;
        UpdateAudioButtonState();
    }

    public void SetAudienceMicrophoneList(string[] devices, string selectedDevice)
    {
        audienceMicListReceived = true;
        audienceMicDevices.Clear();

        // Empty string means default microphone.
        audienceMicDevices.Add("");

        audienceMicrophoneAvailable = devices != null && devices.Length > 0;

        if (audienceMicrophoneAvailable)
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

        if (!audienceMicrophoneAvailable)
        {
            options[0] = "No audience microphone found";
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
        audienceMicDropdown.SetValueWithoutNotify(selectedIndex);
        audienceMicDropdown.RefreshShownValue();
        audienceMicDropdown.interactable = audienceMicrophoneAvailable && !audioStartRequested;

        UpdateAudioButtonState();

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

        string selectedDevice = audienceMicDevices[index];

        MicrophoneDeviceSelectSignal signal = new MicrophoneDeviceSelectSignal
        {
            deviceName = selectedDevice
        };

        Debug.Log(
            "PerformerWebcamControlPanel: Sending audience microphone selection: " +
            (string.IsNullOrEmpty(selectedDevice) ? "Default" : selectedDevice)
        );

        if (!TrySendSignalToAudience("audience_mic_select", JsonUtility.ToJson(signal)))
        {
            Debug.LogWarning("PerformerWebcamControlPanel: Audience microphone selection was not sent because the audience is not connected.");
        }
    }

    private bool TrySendSignalToAudience(string type, string payload)
    {
        if (WebRtcSignalHub.Instance == null)
            return false;

        PlayerRef target = WebRtcSignalHub.Instance.GetOtherPlayer();
        if (target == PlayerRef.None)
            return false;

        WebRtcSignalHub.Instance.SendSignal(target, type, payload);
        return true;
    }

    private void UpdateAudioButtonState()
    {
        bool canStartAudio =
            audienceMicListReceived &&
            actorMicrophoneAvailable &&
            audienceMicrophoneAvailable;

        if (startAudioButton != null)
        {
            startAudioButton.interactable = canStartAudio && !audioStartRequested;
        }

        if (stopAudioButton != null)
        {
            stopAudioButton.interactable = audioStartRequested;
        }

        if (actorMicDropdown != null)
        {
            actorMicDropdown.interactable = actorMicrophoneAvailable && !audioStartRequested;
        }

        if (audienceMicDropdown != null)
        {
            audienceMicDropdown.interactable = audienceMicrophoneAvailable && !audioStartRequested;
        }
    }
}
