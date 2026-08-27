using UnityEngine;

/// <summary>
/// Audience-scene endpoint for the Actor Host's networked H2 visibility state.
/// Kept on an always-active scene object so it can enable an inactive H2 target.
/// </summary>
public class AudienceH2VisibilityTarget : MonoBehaviour
{
    [SerializeField] private GameObject h2Object;

    public void SetVisible(bool visible)
    {
        if (h2Object == null)
        {
            Debug.LogError(
                "AudienceH2VisibilityTarget: H2 object is not assigned in the Audience scene."
            );
            return;
        }

        if (h2Object.activeSelf == visible)
            return;

        h2Object.SetActive(visible);
        Debug.Log(
            "AudienceH2VisibilityTarget: '" + h2Object.name + "' is now " +
            (visible ? "ON." : "OFF.")
        );
    }
}
