using UnityEngine;

public class MultiScreenManager : MonoBehaviour
{
    [Header("Cameras")]
    [SerializeField] private Camera[] cameras;

    // Stores which camera is currently assigned to each display.
    // Example:
    // displayCameraAssignments[0] = 2
    // means Display 1 is using Camera 3.
    private int[] displayCameraAssignments;

    private void Start()
    {
        Debug.Log("Displays connected: " + Display.displays.Length);

        // Activate additional displays.
        for (int i = 1; i < Display.displays.Length; i++)
        {
            Display.displays[i].Activate();
        }

        int usableDisplays = Mathf.Min(Display.displays.Length, cameras.Length);

        displayCameraAssignments = new int[usableDisplays];

        // Default setup:
        // Display 1 -> Camera 1
        // Display 2 -> Camera 2
        // Display 3 -> Camera 3
        for (int i = 0; i < usableDisplays; i++)
        {
            displayCameraAssignments[i] = i;

            cameras[i].targetDisplay = i;
            cameras[i].enabled = true;
        }

        // Disable any cameras that don't currently have a display.
        for (int i = usableDisplays; i < cameras.Length; i++)
        {
            cameras[i].enabled = false;
        }
    }

    public void SetCameraToDisplay(int cameraIndex, int displayIndex)
    {
        if (displayIndex < 0 || displayIndex >= displayCameraAssignments.Length)
        {
            Debug.LogWarning("Invalid display index: " + displayIndex);
            return;
        }

        if (cameraIndex < 0 || cameraIndex >= cameras.Length)
        {
            Debug.LogWarning("Invalid camera index: " + cameraIndex);
            return;
        }

        int currentCameraOnDisplay = displayCameraAssignments[displayIndex];

        // If this display already has the selected camera, do nothing.
        if (currentCameraOnDisplay == cameraIndex)
        {
            return;
        }

        // Find which display is currently using the selected camera.
        int cameraCurrentDisplay = -1;

        for (int i = 0; i < displayCameraAssignments.Length; i++)
        {
            if (displayCameraAssignments[i] == cameraIndex)
            {
                cameraCurrentDisplay = i;
                break;
            }
        }

        // If the selected camera is already being used by another display,
        // swap the two cameras.
        if (cameraCurrentDisplay != -1)
        {
            displayCameraAssignments[displayIndex] = cameraIndex;
            displayCameraAssignments[cameraCurrentDisplay] = currentCameraOnDisplay;

            cameras[cameraIndex].targetDisplay = displayIndex;
            cameras[currentCameraOnDisplay].targetDisplay = cameraCurrentDisplay;

            cameras[cameraIndex].enabled = true;
            cameras[currentCameraOnDisplay].enabled = true;

            Debug.Log(
                "Swapped " +
                cameras[cameraIndex].name +
                " with " +
                cameras[currentCameraOnDisplay].name
            );
        }
        else
        {
            // Camera wasn't currently assigned anywhere.
            cameras[currentCameraOnDisplay].enabled = false;

            displayCameraAssignments[displayIndex] = cameraIndex;

            cameras[cameraIndex].targetDisplay = displayIndex;
            cameras[cameraIndex].enabled = true;
        }

        PrintCurrentSetup();
    }

    private void PrintCurrentSetup()
    {
        for (int i = 0; i < displayCameraAssignments.Length; i++)
        {
            int cameraIndex = displayCameraAssignments[i];

            Debug.Log(
                "Display " + (i + 1) +
                " -> " +
                cameras[cameraIndex].name
            );
        }
    }

    // DISPLAY 1 BUTTONS
    public void Display1Camera1()
    {
        SetCameraToDisplay(0, 0);
    }

    public void Display1Camera2()
    {
        SetCameraToDisplay(1, 0);
    }

    public void Display1Camera3()
    {
        SetCameraToDisplay(2, 0);
    }

    // DISPLAY 2 BUTTONS
    public void Display2Camera1()
    {
        SetCameraToDisplay(0, 1);
    }

    public void Display2Camera2()
    {
        SetCameraToDisplay(1, 1);
    }

    public void Display2Camera3()
    {
        SetCameraToDisplay(2, 1);
    }

    // DISPLAY 3 BUTTONS
    public void Display3Camera1()
    {
        SetCameraToDisplay(0, 2);
    }

    public void Display3Camera2()
    {
        SetCameraToDisplay(1, 2);
    }

    public void Display3Camera3()
    {
        SetCameraToDisplay(2, 2);
    }
}