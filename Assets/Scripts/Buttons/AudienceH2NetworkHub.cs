using Fusion;
using UnityEngine;

/// <summary>
/// Dedicated Fusion control plane for the Audience scene's SimulatedEye H2.
/// The Actor-side button is local UI; only this visibility state is networked.
/// </summary>
public class AudienceH2NetworkHub : NetworkBehaviour
{
    [Networked] public NetworkBool H2Visible { get; private set; }

    private AudienceH2VisibilityTarget visibilityTarget;
    private bool? lastAppliedVisibility;
    private float nextTargetSearchTime;

    public bool IsControlReady =>
        Object != null && Object.IsValid && Object.HasStateAuthority;

    public bool IsH2Visible =>
        Object != null && Object.IsValid && H2Visible;

    public override void Spawned()
    {
        ApplyVisibility();
    }

    public override void Render()
    {
        ApplyVisibility();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        visibilityTarget = null;
        lastAppliedVisibility = null;
    }

    public bool RequestToggleH2Visibility()
    {
        if (!IsControlReady)
        {
            Debug.LogWarning(
                "AudienceH2NetworkHub: Only the Actor Host / State Authority " +
                "can toggle SimulatedEye H2."
            );
            return false;
        }

        H2Visible = !H2Visible;
        ApplyVisibility();

        Debug.Log(
            "AudienceH2NetworkHub: SimulatedEye H2 set to " +
            (H2Visible ? "ON." : "OFF.")
        );
        return true;
    }

    private void ApplyVisibility()
    {
        bool visible = H2Visible;

        if (visibilityTarget == null)
        {
            if (Time.unscaledTime < nextTargetSearchTime)
                return;

            nextTargetSearchTime = Time.unscaledTime + 1f;
            visibilityTarget = FindFirstObjectByType<AudienceH2VisibilityTarget>(
                FindObjectsInactive.Include
            );
        }

        // The Actor scene has the local button, but intentionally no H2 target.
        if (visibilityTarget == null || lastAppliedVisibility == visible)
            return;

        visibilityTarget.SetVisible(visible);
        lastAppliedVisibility = visible;
    }
}
