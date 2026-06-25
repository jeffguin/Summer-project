using UnityEngine;
using UnityEngine.UI;

public class UITestClick : MonoBehaviour
{
    public Button testButton;

    private void Start()
    {
        if (testButton != null)
        {
            testButton.onClick.AddListener(() =>
            {
                Debug.Log("UI button clicked successfully.");
            });
        }
    }
}