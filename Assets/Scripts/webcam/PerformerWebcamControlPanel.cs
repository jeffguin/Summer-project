using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PerformerWebcamControlPanel : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Dropdown cameraDropdown;
    [SerializeField] private Button startButton;
    [SerializeField] private Button stopButton;

    [Header("Runtime State")]
    [SerializeField] private int selectedCameraIndex = 0;

    private NetworkWebcamControlHub controlHub;

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

        SetWaitingState();
    }

    private void OnEnable()
    {
        TryFindControlHub();
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

    public void SetCameraList(string[] cameraNames)
    {
        if (cameraDropdown == null)
            return;

        cameraDropdown.ClearOptions();

        if (cameraNames == null || cameraNames.Length == 0)
        {
            cameraDropdown.AddOptions(new List<string>
            {
                "No audience camera found"
            });

            selectedCameraIndex = 0;

            if (startButton != null)
                startButton.interactable = false;

            cameraDropdown.RefreshShownValue();
            return;
        }

        cameraDropdown.AddOptions(new List<string>(cameraNames));

        selectedCameraIndex = 0;
        cameraDropdown.value = 0;
        cameraDropdown.RefreshShownValue();

        if (startButton != null)
            startButton.interactable = true;

        Debug.Log("PerformerWebcamControlPanel: Audience camera list updated.");
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
}