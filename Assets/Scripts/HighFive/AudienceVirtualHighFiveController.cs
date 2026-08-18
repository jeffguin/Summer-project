#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

using UnityEngine;
using Valve.VR;

[DisallowMultipleComponent]
public sealed class AudienceVirtualHighFiveController : MonoBehaviour
{
    [Header("SteamVR Haptics")]
    [SerializeField] private SteamVR_Action_Vibration hapticAction;
    [SerializeField] private SteamVR_Input_Sources hapticSource =
        SteamVR_Input_Sources.RightHand;

    [Header("Haptic Pulse")]
    [SerializeField, Min(0.01f)] private float hapticDuration = 0.065f;
    [SerializeField, Min(1f)] private float hapticFrequency = 150f;
    [SerializeField, Range(0f, 1f)] private float hapticAmplitude = 0.80f;

    [Header("Diagnostics")]
    [SerializeField] private bool debugLog;

    private bool loggedMissingHapticAction;
    private bool loggedHapticFailure;

    private void Awake()
    {
        ResolveHapticAction();
    }

    private void OnValidate()
    {
        hapticDuration = Mathf.Max(0.01f, hapticDuration);
        hapticFrequency = Mathf.Max(1f, hapticFrequency);
        hapticAmplitude = Mathf.Clamp01(hapticAmplitude);
    }

    /// <summary>
    /// Called only after the Actor Host confirms that the local actor hand
    /// and the synchronized audience right hand share the claphand volume.
    /// </summary>
    public void PlayNetworkClapHaptic()
    {
        if (hapticAction == null)
            ResolveHapticAction();

        if (hapticAction == null)
        {
            if (!loggedMissingHapticAction)
            {
                Debug.LogWarning(
                    "[ClapHaptic] SteamVR action " +
                    "NewSet/highFiveHaptic was not found.",
                    this
                );
                loggedMissingHapticAction = true;
            }

            return;
        }

        try
        {
            hapticAction.Execute(
                0f,
                hapticDuration,
                hapticFrequency,
                hapticAmplitude,
                hapticSource
            );

            if (debugLog)
            {
                Debug.Log(
                    "[ClapHaptic] Audience right-controller haptic played.",
                    this
                );
            }
        }
        catch (System.Exception exception)
        {
            if (loggedHapticFailure)
                return;

            Debug.LogWarning(
                "[ClapHaptic] SteamVR could not play the audience " +
                "controller haptic. " + exception.Message,
                this
            );
            loggedHapticFailure = true;
        }
    }

    private void ResolveHapticAction()
    {
        hapticAction = SteamVR_Input.GetVibrationAction(
            "NewSet",
            "highFiveHaptic"
        );
    }
}

#else

using UnityEngine;

public sealed class AudienceVirtualHighFiveController : MonoBehaviour
{
    public void PlayNetworkClapHaptic()
    {
    }
}

#endif
