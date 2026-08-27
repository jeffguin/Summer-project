using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class PerformerWebcamControlPanel : MonoBehaviour
{
    [Header("Video UI")]
    [SerializeField] private TMP_Dropdown cameraDropdown;
    [SerializeField] private Button startButton;
    [SerializeField] private Button stopButton;
    [SerializeField] private TMP_Text videoStatusText;

    [Header("Multi-Screen Video Layout")]
    [SerializeField, Min(1)] private int selectorRowsPerColumn = 1;
    [SerializeField, Min(1f)] private float selectorRowSpacing = 100f;
    [SerializeField, Min(1f)] private float selectorColumnSpacing = 230f;

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
    private WebRtcVideoReceiver videoReceiver;

    private Coroutine requestAudienceMicListCoroutine;
    private readonly List<string> actorMicDevices = new List<string>();
    private readonly List<string> audienceMicDevices = new List<string>();
    private readonly List<CameraOption> reportedCameraOptions = new List<CameraOption>();
    private readonly List<CameraOption> effectiveCameraOptions = new List<CameraOption>();
    private readonly List<ScreenSelectorBinding> screenSelectorBindings =
        new List<ScreenSelectorBinding>();
    private bool hasAudienceCamera;

    private sealed class CameraOption
    {
        public string streamId;
        public string displayName;
    }

    private sealed class ScreenSelectorBinding
    {
        public VideoDisplayScreen screen;
        public string displayLabel;
        public TMP_Dropdown dropdown;
        public UnityAction<int> listener;
        public bool ownsDropdown;
    }

    private void Awake()
    {
        CreateRuntimeAudioControlsIfNeeded();

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
        TryFindVideoReceiver();
        RefreshActorMicrophoneList();
        TryFindActorAudioEndpoint();
        SubscribeToActorAudioEndpoint();
    }

    private void OnEnable()
    {
        VideoDisplayScreen.RegistryChanged -= OnDisplayScreenRegistryChanged;
        VideoDisplayScreen.RegistryChanged += OnDisplayScreenRegistryChanged;

        TryFindControlHub();
        TryFindVideoReceiver();
        TryFindActorAudioEndpoint();
        SubscribeToActorAudioEndpoint();

        RefreshActorMicrophoneList();
        RequestAudienceMicrophoneList();
        RebuildScreenSelectors();
    }

    private void OnDisable()
    {
        VideoDisplayScreen.RegistryChanged -= OnDisplayScreenRegistryChanged;
        UnsubscribeFromVideoReceiver();

        if (requestAudienceMicListCoroutine != null)
        {
            StopCoroutine(requestAudienceMicListCoroutine);
            requestAudienceMicListCoroutine = null;
        }

        UnsubscribeFromActorAudioEndpoint();
    }

    private void OnDestroy()
    {
        VideoDisplayScreen.RegistryChanged -= OnDisplayScreenRegistryChanged;
        UnsubscribeFromVideoReceiver();
        ClearScreenSelectorBindings();

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

    private void TryFindVideoReceiver()
    {
        WebRtcVideoReceiver found =
            FindFirstObjectByType<WebRtcVideoReceiver>(FindObjectsInactive.Include);

        if (found == videoReceiver)
        {
            SubscribeToVideoReceiver();
            return;
        }

        UnsubscribeFromVideoReceiver();
        videoReceiver = found;
        SubscribeToVideoReceiver();
    }

    private void SubscribeToVideoReceiver()
    {
        if (videoReceiver == null)
            return;

        videoReceiver.AvailableStreamsChanged -= OnAvailableStreamsChanged;
        videoReceiver.AvailableStreamsChanged += OnAvailableStreamsChanged;
        videoReceiver.DisplayScreensChanged -= OnReceiverDisplayScreensChanged;
        videoReceiver.DisplayScreensChanged += OnReceiverDisplayScreensChanged;
    }

    private void UnsubscribeFromVideoReceiver()
    {
        if (videoReceiver == null)
            return;

        videoReceiver.AvailableStreamsChanged -= OnAvailableStreamsChanged;
        videoReceiver.DisplayScreensChanged -= OnReceiverDisplayScreensChanged;
    }

    private void OnAvailableStreamsChanged()
    {
        RefreshEffectiveCameraOptions();
        RebuildScreenSelectors();
    }

    private void OnReceiverDisplayScreensChanged()
    {
        RebuildScreenSelectors();
    }

    private void OnDisplayScreenRegistryChanged()
    {
        TryFindVideoReceiver();
        RebuildScreenSelectors();
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

    private void RefreshEffectiveCameraOptions()
    {
        effectiveCameraOptions.Clear();

        if (videoReceiver != null && videoReceiver.AvailableStreams.Count > 0)
        {
            for (int i = 0; i < videoReceiver.AvailableStreams.Count; i++)
            {
                VideoStreamDescriptor descriptor = videoReceiver.AvailableStreams[i];
                if (descriptor == null || string.IsNullOrEmpty(descriptor.streamId))
                    continue;

                effectiveCameraOptions.Add(new CameraOption
                {
                    streamId = descriptor.streamId,
                    displayName = string.IsNullOrWhiteSpace(descriptor.deviceName)
                        ? "Audience Camera " + (i + 1)
                        : descriptor.deviceName
                });
            }
        }
        else
        {
            effectiveCameraOptions.AddRange(reportedCameraOptions);
        }

        hasAudienceCamera = effectiveCameraOptions.Count > 0;
    }

    private void RebuildScreenSelectors()
    {
        if (cameraDropdown == null)
        {
            Debug.LogError(
                "PerformerWebcamControlPanel: cameraDropdown is NULL. " +
                "Cannot build per-screen camera selectors."
            );
            return;
        }

        RectTransform templateRect = cameraDropdown.transform as RectTransform;
        if (templateRect != null && templateRect.sizeDelta.x < 220f)
            templateRect.sizeDelta = new Vector2(220f, templateRect.sizeDelta.y);

        if (cameraDropdown.captionText != null)
        {
            cameraDropdown.captionText.enableAutoSizing = true;
            cameraDropdown.captionText.fontSizeMin = 10f;
        }

        ClearScreenSelectorBindings();

        List<VideoDisplayScreen> screens = new List<VideoDisplayScreen>();
        if (videoReceiver != null && videoReceiver.DisplayScreens.Count > 0)
        {
            screens.AddRange(videoReceiver.DisplayScreens);
        }
        else
        {
            screens.AddRange(
                FindObjectsByType<VideoDisplayScreen>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                )
            );
            screens.Sort((left, right) =>
                string.Compare(left.ScreenId, right.ScreenId, StringComparison.Ordinal));
        }

        screens.RemoveAll(screen => screen == null || !screen.isActiveAndEnabled);

        if (screens.Count == 0)
        {
            cameraDropdown.gameObject.SetActive(true);
            cameraDropdown.ClearOptions();
            cameraDropdown.AddOptions(new List<string> { "No video display screen found" });
            cameraDropdown.SetValueWithoutNotify(0);
            cameraDropdown.RefreshShownValue();
            cameraDropdown.interactable = false;
            UpdateVideoButtonsForCurrentState();
            return;
        }

        for (int i = 0; i < screens.Count; i++)
        {
            TMP_Dropdown dropdown = i == 0
                ? cameraDropdown
                : CreateScreenSelectorDropdown(i);

            if (dropdown == null)
                continue;

            VideoDisplayScreen screen = screens[i];
            ScreenSelectorBinding binding = new ScreenSelectorBinding
            {
                screen = screen,
                displayLabel = BuildUniqueScreenLabel(screens, i),
                dropdown = dropdown,
                ownsDropdown = i > 0
            };

            ConfigureScreenSelector(binding, i);
            ScreenSelectorBinding capturedBinding = binding;
            binding.listener = value => ApplyScreenCameraSelection(capturedBinding, value);
            dropdown.onValueChanged.AddListener(binding.listener);
            screenSelectorBindings.Add(binding);
        }

        UpdateVideoButtonsForCurrentState();
    }

    private void ConfigureScreenSelector(ScreenSelectorBinding binding, int screenIndex)
    {
        TMP_Dropdown dropdown = binding.dropdown;
        VideoDisplayScreen screen = binding.screen;
        dropdown.gameObject.SetActive(true);
        dropdown.ClearOptions();

        if (effectiveCameraOptions.Count == 0)
        {
            string placeholder = reportedCameraOptions.Count == 0
                ? "Waiting for audience cameras..."
                : "No usable audience camera";
            dropdown.AddOptions(new List<string> { binding.displayLabel + ": " + placeholder });
            dropdown.SetValueWithoutNotify(0);
            dropdown.RefreshShownValue();
            dropdown.interactable = false;
            return;
        }

        List<string> labels = new List<string>(effectiveCameraOptions.Count);
        for (int i = 0; i < effectiveCameraOptions.Count; i++)
        {
            labels.Add(binding.displayLabel + ": " + effectiveCameraOptions[i].displayName);
        }
        dropdown.AddOptions(labels);

        int selectedIndex = FindCameraOptionIndex(screen.SelectedStreamId);
        if (selectedIndex < 0)
            selectedIndex = screenIndex % effectiveCameraOptions.Count;

        dropdown.SetValueWithoutNotify(selectedIndex);
        dropdown.RefreshShownValue();
        dropdown.interactable = hasAudienceCamera;

        ApplyScreenCameraSelection(binding, selectedIndex);
    }

    private static string BuildUniqueScreenLabel(
        IReadOnlyList<VideoDisplayScreen> screens,
        int screenIndex)
    {
        VideoDisplayScreen target = screens[screenIndex];
        string baseName = target.DisplayName;
        int sameNameCount = 0;
        int occurrence = 0;

        for (int i = 0; i < screens.Count; i++)
        {
            if (!string.Equals(screens[i].DisplayName, baseName, StringComparison.Ordinal))
                continue;

            sameNameCount++;
            if (i <= screenIndex)
                occurrence++;
        }

        return sameNameCount > 1 ? baseName + " #" + occurrence : baseName;
    }

    private TMP_Dropdown CreateScreenSelectorDropdown(int screenIndex)
    {
        TMP_Dropdown clone = Instantiate(cameraDropdown, cameraDropdown.transform.parent);
        clone.name = "Video Screen Camera Selector " + (screenIndex + 1);
        clone.onValueChanged = new TMP_Dropdown.DropdownEvent();

        RectTransform sourceRect = cameraDropdown.transform as RectTransform;
        RectTransform cloneRect = clone.transform as RectTransform;
        if (sourceRect != null && cloneRect != null)
        {
            int rows = Mathf.Max(1, selectorRowsPerColumn);
            int row = screenIndex % rows;
            int column = screenIndex / rows;
            cloneRect.anchoredPosition = sourceRect.anchoredPosition + new Vector2(
                selectorColumnSpacing * column,
                selectorRowSpacing * row
            );
        }

        return clone;
    }

    private void ClearScreenSelectorBindings()
    {
        for (int i = 0; i < screenSelectorBindings.Count; i++)
        {
            ScreenSelectorBinding binding = screenSelectorBindings[i];
            if (binding.dropdown != null && binding.listener != null)
                binding.dropdown.onValueChanged.RemoveListener(binding.listener);

            if (binding.ownsDropdown && binding.dropdown != null)
                Destroy(binding.dropdown.gameObject);
        }

        screenSelectorBindings.Clear();
    }

    private int FindCameraOptionIndex(string streamId)
    {
        if (string.IsNullOrEmpty(streamId))
            return -1;

        for (int i = 0; i < effectiveCameraOptions.Count; i++)
        {
            if (effectiveCameraOptions[i].streamId == streamId)
                return i;
        }

        return -1;
    }

    private void ApplyScreenCameraSelection(ScreenSelectorBinding binding, int optionIndex)
    {
        if (binding == null ||
            binding.screen == null ||
            optionIndex < 0 ||
            optionIndex >= effectiveCameraOptions.Count)
        {
            return;
        }

        CameraOption option = effectiveCameraOptions[optionIndex];
        if (screenSelectorBindings.Count > 0 && binding == screenSelectorBindings[0])
            selectedCameraIndex = optionIndex;

        TryFindVideoReceiver();
        if (videoReceiver != null)
            videoReceiver.SetScreenStream(binding.screen, option.streamId);
        else
            binding.screen.SelectStream(option.streamId);

        Debug.Log(
            "PerformerWebcamControlPanel: Screen '" + binding.screen.DisplayName +
            "' selected camera '" + option.displayName + "'."
        );
    }

    private void UpdateVideoButtonsForCurrentState()
    {
        WebRtcVideoReceiver.SessionState state = videoReceiver != null
            ? videoReceiver.State
            : WebRtcVideoReceiver.SessionState.Idle;

        bool idleOrFailed =
            state == WebRtcVideoReceiver.SessionState.Idle ||
            state == WebRtcVideoReceiver.SessionState.Failed;
        bool canStop =
            state == WebRtcVideoReceiver.SessionState.Negotiating ||
            state == WebRtcVideoReceiver.SessionState.Connecting ||
            state == WebRtcVideoReceiver.SessionState.Connected ||
            state == WebRtcVideoReceiver.SessionState.Recovering;

        if (startButton != null)
            startButton.interactable = idleOrFailed && hasAudienceCamera;
        if (stopButton != null)
            stopButton.interactable = canStop;
    }

    public void SetCameraList(string[] cameraNames)
    {
        reportedCameraOptions.Clear();

        if (cameraNames == null || cameraNames.Length == 0)
        {
            selectedCameraIndex = 0;
            hasAudienceCamera = false;
            RefreshEffectiveCameraOptions();
            RebuildScreenSelectors();
            UpdateVideoButtonsForCurrentState();
            return;
        }

        for (int i = 0; i < cameraNames.Length; i++)
        {
            reportedCameraOptions.Add(new CameraOption
            {
                streamId = "camera-" + i,
                displayName = string.IsNullOrWhiteSpace(cameraNames[i])
                    ? "Audience Camera " + (i + 1)
                    : cameraNames[i]
            });
        }

        selectedCameraIndex = 0;
        hasAudienceCamera = true;
        RefreshEffectiveCameraOptions();
        RebuildScreenSelectors();
        UpdateVideoButtonsForCurrentState();

        Debug.Log("PerformerWebcamControlPanel: Audience camera list updated. Count = " + cameraNames.Length);
    }

    private void SetWaitingState()
    {
        reportedCameraOptions.Clear();
        effectiveCameraOptions.Clear();
        RebuildScreenSelectors();

        if (startButton != null)
            startButton.interactable = false;

        if (stopButton != null)
            stopButton.interactable = false;

        hasAudienceCamera = false;
    }

    public void OnCameraSelected(int index)
    {
        selectedCameraIndex = index;

        if (screenSelectorBindings.Count > 0)
            ApplyScreenCameraSelection(screenSelectorBindings[0], index);
    }

    public void OnStartClicked()
    {
        TryFindControlHub();

        if (controlHub == null)
        {
            Debug.LogWarning("PerformerWebcamControlPanel: Cannot start. NetworkWebcamControlHub is missing.");
            return;
        }

        Debug.Log("Performer requested all Audience camera streams.");

        controlHub.RequestStartAllAudienceVideo();
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

    public void SetVideoState(WebRtcVideoReceiver.SessionState state, string message)
    {
        bool idleOrFailed =
            state == WebRtcVideoReceiver.SessionState.Idle ||
            state == WebRtcVideoReceiver.SessionState.Failed;

        bool canStop =
            state == WebRtcVideoReceiver.SessionState.Negotiating ||
            state == WebRtcVideoReceiver.SessionState.Connecting ||
            state == WebRtcVideoReceiver.SessionState.Connected ||
            state == WebRtcVideoReceiver.SessionState.Recovering;

        for (int i = 0; i < screenSelectorBindings.Count; i++)
        {
            TMP_Dropdown dropdown = screenSelectorBindings[i].dropdown;
            if (dropdown != null)
                dropdown.interactable = hasAudienceCamera && effectiveCameraOptions.Count > 0;
        }

        if (startButton != null)
            startButton.interactable = idleOrFailed && hasAudienceCamera;

        if (stopButton != null)
            stopButton.interactable = canStop;

        if (videoStatusText != null)
            videoStatusText.text = "Video: " + state + " — " + message;

        Debug.Log("PerformerWebcamControlPanel: Video state = " + state + ". " + message);
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
        SetAudioStatus("Start requested. Preparing both microphones...");
        Debug.Log("PerformerWebcamControlPanel: Start Audio clicked.");

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
