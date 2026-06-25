using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LocalWebcamManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Dropdown cameraDropdown;
    [SerializeField] private Button startButton;
    [SerializeField] private Button stopButton;

    [Header("Display")]
    [SerializeField] private VideoDisplayScreen videoDisplayScreen;

    [Header("Webcam Settings")]
    [SerializeField] private int requestedWidth = 1280;
    [SerializeField] private int requestedHeight = 720;
    [SerializeField] private int requestedFPS = 30;

    private WebCamDevice[] devices;
    private WebCamTexture webcamTexture;
    private int selectedCameraIndex = 0;

    private void Start()
    {
        if (startButton != null)
        {
            startButton.onClick.AddListener(StartSelectedWebcam);
        }

        if (stopButton != null)
        {
            stopButton.onClick.AddListener(StopWebcam);
        }

        if (cameraDropdown != null)
        {
            cameraDropdown.onValueChanged.AddListener(OnCameraSelected);
        }

        RefreshCameraList();
    }

    private void RefreshCameraList()
    {
        devices = WebCamTexture.devices;

        if (cameraDropdown == null)
        {
            return;
        }

        cameraDropdown.ClearOptions();

        List<string> options = new List<string>();

        for (int i = 0; i < devices.Length; i++)
        {
            options.Add(devices[i].name);
        }

        if (options.Count == 0)
        {
            options.Add("No webcam found");
            Debug.LogWarning("LocalWebcamManager: No webcam found.");
        }

        cameraDropdown.AddOptions(options);

        selectedCameraIndex = FindLikelyExternalCameraIndex();

        cameraDropdown.value = selectedCameraIndex;
        cameraDropdown.RefreshShownValue();
    }

    private int FindLikelyExternalCameraIndex()
    {
        if (devices == null || devices.Length == 0)
        {
            return 0;
        }

        for (int i = 0; i < devices.Length; i++)
        {
            string name = devices[i].name.ToLower();

            bool looksInternal =
                name.Contains("integrated") ||
                name.Contains("built-in") ||
                name.Contains("facetime") ||
                name.Contains("internal");

            if (!looksInternal)
            {
                return i;
            }
        }

        return 0;
    }

    private void OnCameraSelected(int index)
    {
        selectedCameraIndex = index;
    }

    public void StartSelectedWebcam()
    {
        if (devices == null || devices.Length == 0)
        {
            Debug.LogWarning("LocalWebcamManager: No webcam device available.");
            return;
        }

        if (selectedCameraIndex < 0 || selectedCameraIndex >= devices.Length)
        {
            Debug.LogWarning("LocalWebcamManager: Invalid camera index.");
            return;
        }

        StopWebcam();

        string cameraName = devices[selectedCameraIndex].name;

        webcamTexture = new WebCamTexture(
            cameraName,
            requestedWidth,
            requestedHeight,
            requestedFPS
        );

        webcamTexture.Play();

        if (videoDisplayScreen != null)
        {
            videoDisplayScreen.SetTexture(webcamTexture);
        }

        Debug.Log("Started webcam: " + cameraName);
    }

    public WebCamTexture GetCurrentWebcamTexture()
    {
        return webcamTexture;
    }

    public void StopWebcam()
    {
        if (webcamTexture != null)
        {
            if (webcamTexture.isPlaying)
            {
                webcamTexture.Stop();
            }

            webcamTexture = null;
        }

        if (videoDisplayScreen != null)
        {
            videoDisplayScreen.ClearTexture();
        }

        Debug.Log("Webcam stopped.");
    }

    private void OnDestroy()
    {
        StopWebcam();
    }
}