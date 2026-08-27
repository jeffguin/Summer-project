using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Actor-side UI adapter for the Audience H2 visibility state.
/// It discovers the runtime-spawned Fusion hub and only enables the button for
/// the Actor Host that owns State Authority.
/// </summary>
public class PerformerH2VisibilityControlPanel : MonoBehaviour
{
    [SerializeField] private Button h2ToggleButton;
    [SerializeField] private TMP_Text h2ToggleLabel;

    [Min(0.1f)]
    [SerializeField] private float refreshInterval = 0.25f;

    private AudienceH2NetworkHub controlHub;
    private float nextRefreshTime;

    private void Awake()
    {
        if (h2ToggleButton == null)
        {
            Debug.LogError(
                "PerformerH2VisibilityControlPanel: H2 toggle button is not assigned."
            );
            return;
        }

        h2ToggleButton.onClick.RemoveListener(OnH2ToggleClicked);
        h2ToggleButton.onClick.AddListener(OnH2ToggleClicked);
        Refresh();
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
        Refresh();
    }

    private void OnDestroy()
    {
        if (h2ToggleButton != null)
            h2ToggleButton.onClick.RemoveListener(OnH2ToggleClicked);
    }

    private void OnH2ToggleClicked()
    {
        FindControlHub();

        if (controlHub == null || !controlHub.RequestToggleH2Visibility())
        {
            Refresh();
            return;
        }

        Refresh();
    }

    private void Refresh()
    {
        FindControlHub();

        bool ready = controlHub != null && controlHub.IsControlReady;
        bool visible = ready && controlHub.IsH2Visible;

        if (h2ToggleButton != null)
            h2ToggleButton.interactable = ready;

        if (h2ToggleLabel != null)
            h2ToggleLabel.text = "H2: " + (visible ? "ON" : "OFF");
    }

    private void FindControlHub()
    {
        if (controlHub == null)
        {
            controlHub = FindFirstObjectByType<AudienceH2NetworkHub>(
                FindObjectsInactive.Include
            );
        }
    }
}
