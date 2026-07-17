using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;

public class MyGazePointer : MonoBehaviour
{
    public GraphicRaycaster raycaster;
    public EventSystem eventSystem;

    void Update()
    {
        PointerEventData pointerData = new PointerEventData(eventSystem);

        pointerData.position = new Vector2(
            Screen.width / 2,
            Screen.height / 2
        );

        List<RaycastResult> results = new List<RaycastResult>();

        raycaster.Raycast(pointerData, results);

        foreach (RaycastResult result in results)
        {
            Debug.Log("Looking at: " + result.gameObject.name);
        }
    }
}