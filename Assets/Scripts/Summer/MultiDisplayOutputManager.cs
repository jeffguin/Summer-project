using System;
using UnityEngine;

[DefaultExecutionOrder(-10000)]
public class MultiDisplayOutputManager : MonoBehaviour
{
    [Header("Output Layout")]
    [Tooltip("First Unity display API index used for portal output. Index 0 is Inspector Display 1; use index 1 when Inspector Display 1 is a control monitor.")]
    [Min(0)]
    public int firstOutputDisplay = 1;

    [Tooltip("If the requested layout does not fit, use API index 0 (Inspector Display 1) as the first available output.")]
    public bool fallbackToPrimaryDisplay = true;

    [Header("Portal Cameras")]
    [Tooltip("Optional explicit camera order. If empty, all PortalCameraController instances are found and sorted by name.")]
    public PortalCameraController[] orderedPortalCameras = Array.Empty<PortalCameraController>();

    [Tooltip("Enable portal camera GameObjects after assigning their output displays.")]
    public bool enablePortalCameraObjects = true;

    [Tooltip("Enable the physical screen reference objects used to calculate each frustum.")]
    public bool enablePhysicalScreenObjects = true;

    private void Awake()
    {
        PortalCameraController[] portalCameras = ResolvePortalCameras();
        LogDetectedDisplays();

        if (portalCameras.Length == 0)
        {
            Debug.LogWarning("Multi-display setup found no PortalCameraController instances.", this);
            return;
        }

        int outputStart = ResolveOutputStart(portalCameras.Length);
        int availableOutputCount = Mathf.Max(0, Display.displays.Length - outputStart);
        int activeOutputCount = Mathf.Min(portalCameras.Length, availableOutputCount);

        ActivateOutputDisplays(outputStart, activeOutputCount);

        for (int i = 0; i < portalCameras.Length; i++)
        {
            PortalCameraController portalController = portalCameras[i];
            if (portalController == null)
                continue;

            if (i >= activeOutputCount)
            {
                portalController.gameObject.SetActive(false);
                Debug.LogError(
                    $"No display is available for portal camera '{portalController.name}'. " +
                    $"Detected {Display.displays.Length} display(s).",
                    portalController);
                continue;
            }

            int displayIndex = outputStart + i;
            ConfigurePortalCamera(portalController, displayIndex);
        }
    }

    private PortalCameraController[] ResolvePortalCameras()
    {
        PortalCameraController[] result;

        if (orderedPortalCameras != null && orderedPortalCameras.Length > 0)
        {
            result = (PortalCameraController[])orderedPortalCameras.Clone();
        }
        else
        {
            result = FindObjectsByType<PortalCameraController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            Array.Sort(
                result,
                (left, right) => string.Compare(
                    left != null ? left.name : string.Empty,
                    right != null ? right.name : string.Empty,
                    StringComparison.Ordinal));
        }

        return result;
    }

    private int ResolveOutputStart(int portalCameraCount)
    {
        int requestedStart = Mathf.Max(0, firstOutputDisplay);
        bool requestedLayoutFits = requestedStart + portalCameraCount <= Display.displays.Length;

        if (requestedLayoutFits)
            return requestedStart;

        // A partial fallback is still useful. With one detected display, keep the
        // first portal camera on the primary display and disable only the rest.
        if (fallbackToPrimaryDisplay && Display.displays.Length > 0)
        {
            Debug.LogWarning(
                $"The requested portal layout starts at display API index {requestedStart}, but only " +
                $"{Display.displays.Length} display(s) were detected. Available portal output will " +
                "start at API index 0 (Inspector Display 1).",
                this);
            return 0;
        }

        return requestedStart;
    }

    private void ActivateOutputDisplays(int outputStart, int outputCount)
    {
        for (int i = 0; i < outputCount; i++)
        {
            int displayIndex = outputStart + i;

            // API index 0 is Inspector Display 1, the primary display, and is already active.
            if (displayIndex == 0 || Display.displays[displayIndex].active)
                continue;

#if UNITY_EDITOR
            Debug.Log(
                $"Display {displayIndex} will be activated by the standalone build.",
                this);
#else
            Display.displays[displayIndex].Activate();
#endif
        }
    }

    private void ConfigurePortalCamera(
        PortalCameraController portalController,
        int displayIndex)
    {
        Camera portalCamera = portalController.GetComponent<Camera>();
        if (portalCamera == null)
        {
            Debug.LogError(
                $"Portal camera '{portalController.name}' has no Camera component.",
                portalController);
            portalController.gameObject.SetActive(false);
            return;
        }

        portalCamera.targetDisplay = displayIndex;
        portalCamera.enabled = true;
        portalController.enabled = true;

        if (enablePhysicalScreenObjects && portalController.screen != null)
            portalController.screen.gameObject.SetActive(true);

        if (enablePortalCameraObjects)
            portalController.gameObject.SetActive(true);

        Debug.Log(
            $"Portal camera '{portalController.name}' assigned to Display {displayIndex} " +
            $"({Display.displays[displayIndex].systemWidth}x" +
            $"{Display.displays[displayIndex].systemHeight}).",
            portalController);
    }

    private void LogDetectedDisplays()
    {
        Debug.Log($"Unity detected {Display.displays.Length} display(s).", this);

        for (int i = 0; i < Display.displays.Length; i++)
        {
            Display display = Display.displays[i];
            Debug.Log(
                $"Display {i}: {display.systemWidth}x{display.systemHeight}, " +
                $"active={display.active}",
                this);
        }
    }
}
