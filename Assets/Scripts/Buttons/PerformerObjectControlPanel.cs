using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 演员端独立的网络物体控制面板。
/// 当前只提供 Reset All，并把执行权限制在 Actor Host / State Authority。
/// </summary>
public class PerformerObjectControlPanel : MonoBehaviour
{
    [Header("Reset All UI")]
    [Tooltip("可选的按钮样式模板。未指定时会查找同级 StopButton。")]
    [SerializeField] private Button buttonTemplate;

    [Tooltip("可预先指定 Reset All 按钮；未指定时会在运行时从模板复制。")]
    [SerializeField] private Button resetAllButton;

    [SerializeField] private Vector2 resetButtonAnchoredPosition =
        new Vector2(203f, -150f);

    [Header("Actor Host")]
    [SerializeField] private BasicSpawner basicSpawner;

    [Min(0.1f)]
    [SerializeField] private float refreshInterval = 0.25f;

    private float nextRefreshTime;

    private void Awake()
    {
        CreateResetAllButtonIfNeeded();

        if (resetAllButton != null)
        {
            resetAllButton.onClick.RemoveListener(OnResetAllClicked);
            resetAllButton.onClick.AddListener(OnResetAllClicked);
        }

        RefreshButtonState();
    }

    private void OnEnable()
    {
        nextRefreshTime = 0f;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime = Time.unscaledTime + refreshInterval;
        RefreshButtonState();
    }

    private void OnDestroy()
    {
        if (resetAllButton != null)
        {
            resetAllButton.onClick.RemoveListener(OnResetAllClicked);
        }
    }

    private void CreateResetAllButtonIfNeeded()
    {
        if (resetAllButton == null)
        {
            Transform existingButton = transform.Find("ResetAllObjectsButton");

            if (existingButton != null)
            {
                resetAllButton = existingButton.GetComponent<Button>();
            }
        }

        if (resetAllButton == null && buttonTemplate == null)
        {
            Transform stopButton = transform.Find("StopButton");

            if (stopButton != null)
            {
                buttonTemplate = stopButton.GetComponent<Button>();
            }
        }

        if (resetAllButton == null && buttonTemplate != null)
        {
            resetAllButton = Instantiate(buttonTemplate, buttonTemplate.transform.parent);
            resetAllButton.gameObject.name = "ResetAllObjectsButton";
            resetAllButton.onClick = new Button.ButtonClickedEvent();
        }

        if (resetAllButton == null)
        {
            Debug.LogError(
                "PerformerObjectControlPanel: Unable to create Reset All button. " +
                "Assign a Button template in the performer menu prefab."
            );
            return;
        }

        RectTransform buttonRect = resetAllButton.transform as RectTransform;

        if (buttonRect != null)
        {
            buttonRect.anchoredPosition = resetButtonAnchoredPosition;
        }

        TMP_Text buttonLabel = resetAllButton.GetComponentInChildren<TMP_Text>(true);

        if (buttonLabel != null)
        {
            buttonLabel.text = "Reset All";
        }
    }

    private void TryFindBasicSpawner()
    {
        if (basicSpawner == null)
        {
            basicSpawner = FindFirstObjectByType<BasicSpawner>();
        }
    }

    private void RefreshButtonState()
    {
        TryFindBasicSpawner();

        if (resetAllButton == null)
            return;

        resetAllButton.interactable =
            basicSpawner != null &&
            basicSpawner.IsActorHostReadyForObjectReset &&
            basicSpawner.SpawnedNetworkInteractableCount > 0;
    }

    private void OnResetAllClicked()
    {
        TryFindBasicSpawner();

        if (basicSpawner == null || !basicSpawner.IsActorHostReadyForObjectReset)
        {
            Debug.LogWarning(
                "PerformerObjectControlPanel: Reset All is unavailable because " +
                "the Actor Host / State Authority is not ready."
            );
            RefreshButtonState();
            return;
        }

        int resetCount = basicSpawner.ResetAllNetworkInteractables();

        Debug.Log(
            $"PerformerObjectControlPanel: Reset All requested. " +
            $"Successfully reset {resetCount} object(s)."
        );

        RefreshButtonState();
    }
}
