using System.Collections.Generic;
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
    [SerializeField] private Button startAudioButton;
    [SerializeField] private Button stopAudioButton;
    [SerializeField] private TMP_Text audioStatusText;

    [Header("Actor Audio Endpoint")]
    [SerializeField] private WebRtcAudioEndpoint actorAudioEndpoint;

    [Header("Runtime State")]
    [SerializeField] private int selectedCameraIndex = 0;
    [SerializeField] private int selectedActorMicIndex = 0;
    [SerializeField] private int selectedAudienceMicIndex = 0;

    private NetworkWebcamControlHub controlHub;

    private Coroutine requestAudienceMicListCoroutine;
    private readonly List<string> actorMicDevices = new List<string>();
    private readonly List<string> audienceMicDevices = new List<string>();

    private void Awake()
    {
        CreateRuntimeAudioControlsIfNeeded();

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
        RefreshActorMicrophoneList();
        TryFindActorAudioEndpoint();
        SubscribeToActorAudioEndpoint();
    }

    private void OnEnable()
    {
        TryFindControlHub();
        TryFindActorAudioEndpoint();
        SubscribeToActorAudioEndpoint();

        RefreshActorMicrophoneList();
        RequestAudienceMicrophoneList();
    }

    private void OnDisable()
    {
        if (requestAudienceMicListCoroutine != null)
        {
            StopCoroutine(requestAudienceMicListCoroutine);
            requestAudienceMicListCoroutine = null;
        }

        UnsubscribeFromActorAudioEndpoint();
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

        UnsubscribeFromActorAudioEndpoint();

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

    private void TryFindActorAudioEndpoint()
    {
        if (actorAudioEndpoint != null)
            return;

        WebRtcAudioEndpoint[] endpoints =
            FindObjectsByType<WebRtcAudioEndpoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (WebRtcAudioEndpoint endpoint in endpoints)
        {
            if (endpoint.Role == WebRtcAudioEndpoint.EndpointRole.Actor)
            {
                actorAudioEndpoint = endpoint;
                break;
            }
        }

        if (actorAudioEndpoint == null)
            Debug.LogWarning("PerformerWebcamControlPanel: Actor WebRtcAudioEndpoint not found yet.");
    }

    private void SubscribeToActorAudioEndpoint()
    {
        if (actorAudioEndpoint == null)
            return;

        actorAudioEndpoint.AudienceMicrophoneListReceived -= SetAudienceMicrophoneList;
        actorAudioEndpoint.AudienceMicrophoneListReceived += SetAudienceMicrophoneList;
        actorAudioEndpoint.AudienceMicrophoneSelectionAcknowledged -= OnAudienceMicrophoneSelectionAcknowledged;
        actorAudioEndpoint.AudienceMicrophoneSelectionAcknowledged += OnAudienceMicrophoneSelectionAcknowledged;
        actorAudioEndpoint.StateChanged -= OnAudioStateChanged;
        actorAudioEndpoint.StateChanged += OnAudioStateChanged;

        OnAudioStateChanged(actorAudioEndpoint.State, "Audio endpoint ready.");
    }

    private void UnsubscribeFromActorAudioEndpoint()
    {
        if (actorAudioEndpoint == null)
            return;

        actorAudioEndpoint.AudienceMicrophoneListReceived -= SetAudienceMicrophoneList;
        actorAudioEndpoint.AudienceMicrophoneSelectionAcknowledged -= OnAudienceMicrophoneSelectionAcknowledged;
        actorAudioEndpoint.StateChanged -= OnAudioStateChanged;
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

        TryFindActorAudioEndpoint();

        string[] devices = actorAudioEndpoint != null
            ? actorAudioEndpoint.GetLocalMicrophoneDevices()
            : Microphone.devices;

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
        actorMicDropdown.SetValueWithoutNotify(0);
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
            TryFindActorAudioEndpoint();

            if (actorAudioEndpoint == null)
            {
                Debug.LogWarning(
                    "PerformerWebcamControlPanel: Actor audio endpoint is missing. " +
                    "Retry audience microphone list request."
                );

                retryCount++;
                yield return new WaitForSeconds(1f);
                continue;
            }

            SubscribeToActorAudioEndpoint();

            if (!actorAudioEndpoint.RequestAudienceMicrophoneList())
            {
                Debug.LogWarning(
                    "PerformerWebcamControlPanel: No audience player found. " +
                    "Retry audience microphone list request."
                );

                retryCount++;
                yield return new WaitForSeconds(1f);
                continue;
            }

            Debug.Log("PerformerWebcamControlPanel: Requested the Audience microphone list.");

            requestAudienceMicListCoroutine = null;
            yield break;
        }

        Debug.LogWarning("PerformerWebcamControlPanel: Failed to request audience microphone list after retries.");

        requestAudienceMicListCoroutine = null;
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
        audienceMicDropdown.SetValueWithoutNotify(selectedIndex);
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

        TryFindActorAudioEndpoint();

        if (actorAudioEndpoint != null)
        {
            actorAudioEndpoint.SetMicrophoneDeviceName(selectedDevice);
        }
        else
        {
            Debug.LogWarning("PerformerWebcamControlPanel: Actor audio endpoint is missing. Actor microphone selection was not applied.");
        }
    }

    public void OnAudienceMicrophoneSelected(int index)
    {
        if (index < 0 || index >= audienceMicDevices.Count)
            return;

        selectedAudienceMicIndex = index;

        TryFindActorAudioEndpoint();

        if (actorAudioEndpoint == null)
        {
            Debug.LogWarning("PerformerWebcamControlPanel: Cannot select Audience microphone. Actor audio endpoint is missing.");
            return;
        }

        string selectedDevice = audienceMicDevices[index];

        Debug.Log(
            "PerformerWebcamControlPanel: Sending audience microphone selection: " +
            (string.IsNullOrEmpty(selectedDevice) ? "Default" : selectedDevice)
        );

        if (!actorAudioEndpoint.SelectAudienceMicrophone(selectedDevice))
            Debug.LogWarning("PerformerWebcamControlPanel: Audience microphone selection could not be sent.");
    }

    public void OnStartAudioClicked()
    {
        TryFindControlHub();
        TryFindActorAudioEndpoint();

        if (controlHub != null)
        {
            controlHub.RequestStartAudienceAudio();
            return;
        }

        if (actorAudioEndpoint != null)
        {
            actorAudioEndpoint.StartAudioSession();
            return;
        }

        SetAudioStatus("Cannot start audio: Actor audio endpoint is missing.");
    }

    public void OnStopAudioClicked()
    {
        TryFindControlHub();
        TryFindActorAudioEndpoint();

        if (controlHub != null)
        {
            controlHub.RequestStopAudienceAudio();
            return;
        }

        if (actorAudioEndpoint != null)
        {
            actorAudioEndpoint.StopAudioSession();
            return;
        }

        SetAudioStatus("Cannot stop audio: Actor audio endpoint is missing.");
    }

    private void OnAudienceMicrophoneSelectionAcknowledged(
        string deviceName,
        bool success,
        string message)
    {
        string label = string.IsNullOrEmpty(deviceName) ? "Default Audience Microphone" : deviceName;
        SetAudioStatus(
            success
                ? "Audience microphone selected: " + label
                : "Audience microphone selection failed: " + message
        );
    }

    private void OnAudioStateChanged(WebRtcAudioEndpoint.SessionState state, string message)
    {
        SetAudioStatus(state + ": " + message);

        if (startAudioButton != null)
        {
            startAudioButton.interactable =
                state == WebRtcAudioEndpoint.SessionState.Idle ||
                state == WebRtcAudioEndpoint.SessionState.Failed;
        }

        if (stopAudioButton != null)
        {
            stopAudioButton.interactable =
                state != WebRtcAudioEndpoint.SessionState.Idle &&
                state != WebRtcAudioEndpoint.SessionState.WaitingForSignalHub;
        }
    }

    private void SetAudioStatus(string message)
    {
        if (audioStatusText != null)
            audioStatusText.text = message;

        Debug.Log("PerformerWebcamControlPanel: Audio status = " + message);
    }

    private void CreateRuntimeAudioControlsIfNeeded()
    {
        if (refreshAudioDevicesButton == null)
            return;

        if (startAudioButton == null)
            startAudioButton = CloneAudioButton(refreshAudioDevicesButton, "Start Audio", 1);

        if (stopAudioButton == null)
            stopAudioButton = CloneAudioButton(refreshAudioDevicesButton, "Stop Audio", 2);

        if (audioStatusText == null)
            audioStatusText = CreateAudioStatusText(refreshAudioDevicesButton, 3);
    }

    private static Button CloneAudioButton(Button template, string label, int rowOffset)
    {
        Button clone = Instantiate(template, template.transform.parent);
        clone.name = label + " Button";
        clone.onClick = new Button.ButtonClickedEvent();

        RectTransform sourceRect = template.transform as RectTransform;
        RectTransform cloneRect = clone.transform as RectTransform;

        if (sourceRect != null && cloneRect != null)
        {
            float height = Mathf.Max(Mathf.Abs(sourceRect.sizeDelta.y), sourceRect.rect.height);
            float step = Mathf.Max(8f, height * 1.15f);
            cloneRect.anchoredPosition = sourceRect.anchoredPosition + Vector2.down * step * rowOffset;
        }

        TMP_Text text = clone.GetComponentInChildren<TMP_Text>(true);
        if (text != null)
            text.text = label;

        return clone;
    }

    private static TMP_Text CreateAudioStatusText(Button template, int rowOffset)
    {
        RectTransform sourceRect = template.transform as RectTransform;
        TMP_Text templateText = template.GetComponentInChildren<TMP_Text>(true);

        GameObject statusObject = new GameObject(
            "Audio Status Text",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI)
        );
        statusObject.transform.SetParent(template.transform.parent, false);

        RectTransform statusRect = statusObject.GetComponent<RectTransform>();
        TextMeshProUGUI statusText = statusObject.GetComponent<TextMeshProUGUI>();

        if (sourceRect != null)
        {
            float height = Mathf.Max(Mathf.Abs(sourceRect.sizeDelta.y), sourceRect.rect.height);
            float step = Mathf.Max(8f, height * 1.15f);
            statusRect.anchorMin = sourceRect.anchorMin;
            statusRect.anchorMax = sourceRect.anchorMax;
            statusRect.pivot = sourceRect.pivot;
            statusRect.sizeDelta = new Vector2(sourceRect.sizeDelta.x * 2f, sourceRect.sizeDelta.y);
            statusRect.anchoredPosition = sourceRect.anchoredPosition + Vector2.down * step * rowOffset;
        }

        if (templateText != null)
        {
            statusText.font = templateText.font;
            statusText.fontSize = templateText.fontSize;
            statusText.color = templateText.color;
        }

        statusText.alignment = TextAlignmentOptions.Center;
        statusText.text = "Audio: Idle";
        statusText.raycastTarget = false;
        return statusText;
    }
}
