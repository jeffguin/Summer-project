using UnityEngine;

public class DisplayScreenCountk : MonoBehaviour
{
    void Start()
    {
        Debug.Log("Displays connected: " + Display.displays.Length);

        for (int i = 0; i < Display.displays.Length; i++)
        {
            Debug.Log($"Display {i}: {Display.displays[i].systemWidth}x{Display.displays[i].systemHeight}");
        }
    }
}
