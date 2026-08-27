using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 演员端独立的网络物体控制面板。
/// 当前只提供 Reset All，并把执行权限制在 Actor Host / State Authority。
/// </summary>
public class PerformerObjectControlPanel : MonoBehaviour
{
    [Header("Reset All UI")]
    [Tooltip("Actor 场景 Canvas 中预先创建的 Reset All 按钮。")]
    [SerializeField] private Button resetAllButton;

    [Header("Actor Host")]
    [SerializeField] private BasicSpawner basicSpawner;

    [Min(0.1f)]
    [SerializeField] private float refreshInterval = 0.25f;

    private float nextRefreshTime;

    private void Awake()
    {
        if (resetAllButton == null)
        {
            Debug.LogError(
                "PerformerObjectControlPanel: Reset All button is not assigned. " +
                "Add the button to the Actor scene Canvas and assign it in the Inspector."
            );
        }
        else
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
