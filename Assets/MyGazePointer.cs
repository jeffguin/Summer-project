using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;

public class MyGazePointer : MonoBehaviour
{
    [Header("References")]
    public GraphicRaycaster raycaster;
    public EventSystem eventSystem;

    [Header("Settings")]
    public float dwellTime = 1.5f;

    private Button currentButton;
    private float timer;

    void Update()
    {
        PointerEventData pointerData = new PointerEventData(eventSystem);
        pointerData.position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        List<RaycastResult> results = new List<RaycastResult>();
        raycaster.Raycast(pointerData, results);

        Button lookedAtButton = null;

        foreach (RaycastResult result in results)
        {
            lookedAtButton = result.gameObject.GetComponent<Button>();

            if (lookedAtButton != null)
                break;
        }

        if (lookedAtButton != currentButton)
        {
            currentButton = lookedAtButton;
            timer = 0f;
        }

        if (currentButton != null)
        {
            timer += Time.deltaTime;

            if (timer >= dwellTime)
            {
                currentButton.onClick.Invoke();

                // Prevent repeated clicking while still looking
                timer = 0f;
                currentButton = null;
            }
        }
    }
}