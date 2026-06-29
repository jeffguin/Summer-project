using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class WebcamScreenManager : MonoBehaviour
{
    [Header("Display Target")]
    public Renderer targetRenderer;
    public RawImage targetRawImage;

    [Header("Camera Selection UI")]
    public TMP_Dropdown cameraDropdown;
    public Button refreshButton;

    [Header("Webcam Settings")]
    public int requestedWidth = 1280;
    public int requestedHeight = 720;
    public int requestedFPS = 30;

    private WebCamTexture webcamTexture;
    private WebCamDevice[] devices;

    void Start()
    {
        if (refreshButton != null)
        {
            refreshButton.onClick.AddListener(RefreshCameraList);
        }

        if (cameraDropdown != null)
        {
            cameraDropdown.onValueChanged.AddListener(SelectCamera);
        }

        RefreshCameraList();
    }

    public void RefreshCameraList()
    {
        devices = WebCamTexture.devices;

        if (cameraDropdown != null)
        {
            cameraDropdown.ClearOptions();

            List<string> options = new List<string>();

            for (int i = 0; i < devices.Length; i++)
            {
                options.Add(devices[i].name);
            }

            cameraDropdown.AddOptions(options);
        }

        if (devices.Length == 0)
        {
            Debug.LogWarning("No webcam found.");
            return;
        }

        int defaultCameraIndex = FindExternalCameraIndex();
        SelectCamera(defaultCameraIndex);

        if (cameraDropdown != null)
        {
            cameraDropdown.value = defaultCameraIndex;
        }
    }

    private int FindExternalCameraIndex()
    {
        for (int i = 0; i < devices.Length; i++)
        {
            string name = devices[i].name.ToLower();

            if (!name.Contains("integrated") &&
                !name.Contains("built-in") &&
                !name.Contains("facetime") &&
                !name.Contains("internal"))
            {
                return i;
            }
        }

        return 0;
    }

    public void SelectCamera(int index)
    {
        if (devices == null || devices.Length == 0)
        {
            return;
        }

        if (index < 0 || index >= devices.Length)
        {
            return;
        }

        StopCurrentCamera();

        string selectedCameraName = devices[index].name;

        webcamTexture = new WebCamTexture(
            selectedCameraName,
            requestedWidth,
            requestedHeight,
            requestedFPS
        );

        if (targetRenderer != null)
        {
            targetRenderer.material.mainTexture = webcamTexture;
        }

        if (targetRawImage != null)
        {
            targetRawImage.texture = webcamTexture;
        }

        webcamTexture.Play();

        Debug.Log("Selected webcam: " + selectedCameraName);
    }

    private void StopCurrentCamera()
    {
        if (webcamTexture != null)
        {
            if (webcamTexture.isPlaying)
            {
                webcamTexture.Stop();
            }

            webcamTexture = null;
        }
    }

    void OnDestroy()
    {
        StopCurrentCamera();
    }
}