using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LocalWebcamManager : MonoBehaviour
{
    public sealed class CameraCapture
    {
        internal CameraCapture(int cameraIndex, string deviceName, WebCamTexture texture)
        {
            CameraIndex = cameraIndex;
            DeviceName = deviceName;
            Texture = texture;
        }

        public int CameraIndex { get; }
        public string DeviceName { get; }
        public string StreamId => "camera-" + CameraIndex;
        public WebCamTexture Texture { get; }
    }

    [Header("Optional Local UI")]
    [SerializeField] private TMP_Dropdown cameraDropdown;
    [SerializeField] private Button startButton;
    [SerializeField] private Button stopButton;

    [Header("Optional Local Preview Display")]
    [SerializeField] private VideoDisplayScreen videoDisplayScreen;

    [Header("Webcam Settings")]
    [SerializeField] private int requestedWidth = 1280;
    [SerializeField] private int requestedHeight = 720;
    [SerializeField] private int requestedFPS = 30;

    private WebCamDevice[] devices;
    private int selectedCameraIndex = 0;
    private readonly List<CameraCapture> activeCaptures = new List<CameraCapture>();

    public IReadOnlyList<CameraCapture> ActiveCaptures => activeCaptures;
    public string CurrentDeviceName =>
        activeCaptures.Count > 0 ? activeCaptures[0].DeviceName : "";
    public bool IsCurrentCameraReady =>
        activeCaptures.Count > 0 && IsCameraReady(activeCaptures[0]);

    private void Start()
    {
        RefreshCameraList();

        // These UI bindings are optional.
        // In the final Audience Client flow, these buttons will usually be null or inactive.
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
    }

    public void RefreshCameraList()
    {
        devices = WebCamTexture.devices;

        Debug.Log("Webcam device count: " + devices.Length);

        for (int i = 0; i < devices.Length; i++)
        {
            Debug.Log("Webcam " + i + ": " + devices[i].name);
        }
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

    public string[] GetCameraNames()
    {
        devices = WebCamTexture.devices;

        string[] names = new string[devices.Length];

        for (int i = 0; i < devices.Length; i++)
        {
            names[i] = devices[i].name;
        }

        return names;
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
        StartCameraByIndex(selectedCameraIndex);
    }

    public void StartCameraByIndex(int cameraIndex)
    {
        if (!TryStartCameraByIndex(cameraIndex, out string error))
            Debug.LogWarning("LocalWebcamManager: " + error);
    }

    public bool TryStartCameraByIndex(int cameraIndex, out string error)
    {
        error = "";
        devices = WebCamTexture.devices;

        if (devices == null || devices.Length == 0)
        {
            error = "No webcam device is available.";
            return false;
        }

        if (cameraIndex < 0 || cameraIndex >= devices.Length)
        {
            error = "Invalid camera index: " + cameraIndex + ". Device count: " + devices.Length + ".";
            return false;
        }

        selectedCameraIndex = cameraIndex;

        StopWebcam();

        string cameraName = devices[selectedCameraIndex].name;

        WebCamTexture webcamTexture;
        try
        {
            webcamTexture = CreateAndPlayTexture(cameraName);
        }
        catch (System.Exception exception)
        {
            error = "Could not start camera '" + cameraName + "': " + exception.Message;
            return false;
        }

        CameraCapture capture = new CameraCapture(selectedCameraIndex, cameraName, webcamTexture);
        activeCaptures.Add(capture);

        // Local preview is optional.
        // In final audience flow, this can be null.
        if (videoDisplayScreen != null)
        {
            videoDisplayScreen.SetTexture(webcamTexture);
        }

        Debug.Log("Started webcam: " + cameraName);
        return true;
    }

    public bool TryStartAllCameras(out IReadOnlyList<CameraCapture> captures, out string error)
    {
        error = "";
        devices = WebCamTexture.devices;
        StopWebcam();

        if (devices == null || devices.Length == 0)
        {
            captures = activeCaptures;
            error = "No webcam device is available.";
            return false;
        }

        List<string> failures = new List<string>();

        for (int i = 0; i < devices.Length; i++)
        {
            string cameraName = devices[i].name;

            try
            {
                WebCamTexture texture = CreateAndPlayTexture(cameraName);
                activeCaptures.Add(new CameraCapture(i, cameraName, texture));
                Debug.Log("LocalWebcamManager: Started camera " + i + ": " + cameraName);
            }
            catch (System.Exception exception)
            {
                failures.Add(cameraName + ": " + exception.Message);
                Debug.LogWarning(
                    "LocalWebcamManager: Could not start camera " + i + " ('" +
                    cameraName + "'): " + exception.Message
                );
            }
        }

        if (videoDisplayScreen != null && activeCaptures.Count > 0)
            videoDisplayScreen.SetTexture(activeCaptures[0].Texture);

        captures = activeCaptures;

        if (activeCaptures.Count == 0)
        {
            error = failures.Count > 0
                ? "No camera could be started. " + string.Join("; ", failures)
                : "No camera could be started.";
            return false;
        }

        if (failures.Count > 0)
            error = "Some cameras could not be started: " + string.Join("; ", failures);

        return true;
    }

    public bool IsCameraReady(CameraCapture capture)
    {
        WebCamTexture texture = capture != null ? capture.Texture : null;
        return texture != null &&
               texture.isPlaying &&
               texture.didUpdateThisFrame &&
               texture.width > 16 &&
               texture.height > 16;
    }

    public void StopCapture(CameraCapture capture)
    {
        if (capture == null)
            return;

        bool wasPreviewed =
            videoDisplayScreen != null && videoDisplayScreen.CurrentTexture == capture.Texture;
        WebCamTexture texture = capture.Texture;
        if (texture != null && texture.isPlaying)
            texture.Stop();

        activeCaptures.Remove(capture);

        if (wasPreviewed && videoDisplayScreen != null)
        {
            if (activeCaptures.Count > 0)
                videoDisplayScreen.SetTexture(activeCaptures[0].Texture);
            else
                videoDisplayScreen.ClearTexture();
        }
    }

    public WebCamTexture GetCurrentWebcamTexture()
    {
        return activeCaptures.Count > 0 ? activeCaptures[0].Texture : null;
    }

    public void StopWebcam()
    {
        for (int i = activeCaptures.Count - 1; i >= 0; i--)
        {
            WebCamTexture texture = activeCaptures[i].Texture;
            if (texture != null && texture.isPlaying)
                texture.Stop();
        }

        activeCaptures.Clear();

        if (videoDisplayScreen != null)
        {
            videoDisplayScreen.ClearTexture();
        }

        Debug.Log("Webcam stopped.");
    }

    private WebCamTexture CreateAndPlayTexture(string cameraName)
    {
        WebCamTexture texture = new WebCamTexture(
            cameraName,
            requestedWidth,
            requestedHeight,
            requestedFPS
        );
        texture.Play();
        return texture;
    }

    private void OnDestroy()
    {
        StopWebcam();
    }

    private void OnDisable()
    {
        StopWebcam();
    }
}
