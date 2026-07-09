using UnityEngine;

public class MultiScreenManager : MonoBehaviour
{
    void Start()
    {
        Debug.Log("Displays connected: " + Display.displays.Length);

        // Display 1 is activated automatically.
        // Activate Display 2, 3, 4, etc.
        for (int i = 1; i < Display.displays.Length; i++)
        {
            Display.displays[i].Activate();
        }
    }
}