using System.Collections.Generic;
using UnityEngine;

public class ToggleObjectList : MonoBehaviour
{
    [Header("Objects to Toggle")]
    [SerializeField]
    private List<GameObject> objectsToToggle = new List<GameObject>();

    [Header("Settings")]
    [SerializeField]
    private bool startActive = false;

    private bool isActive;

    private void Start()
    {
        isActive = startActive;
        SetObjectsActive(isActive);
    }

    // OnClick() this one
    public void ToggleObjects()
    {
        isActive = !isActive;
        SetObjectsActive(isActive);
    }

    public void ShowObjects()
    {
        isActive = true;
        SetObjectsActive(true);
    }

    public void HideObjects()
    {
        isActive = false;
        SetObjectsActive(false);
    }

    private void SetObjectsActive(bool active)
    {
        foreach (GameObject obj in objectsToToggle)
        {
            if (obj != null)
            {
                obj.SetActive(active);
            }
        }
    }
}