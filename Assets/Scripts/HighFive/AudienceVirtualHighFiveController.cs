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

    [Tooltip("观众电脑窗口获得焦点时，按此键可绕过网络直接测试右手柄振动。")]
    [SerializeField] private KeyCode localTestKey = KeyCode.H;

    [SerializeField] private bool enableLocalKeyboardTest = true;

    [Header("Vive Compatibility Fallback")]
    [Tooltip("除 SteamVR Action 外，再向已追踪的 Vive 右手设备发送一次传统脉冲，避免旧绑定缺少输出 Action 时完全无振动。")]
    [SerializeField] private bool useLegacyVivePulseFallback = true;

    [SerializeField, Range(100, 3999)]
    private int legacyPulseMicroseconds = 1800;

    private bool loggedMissingHapticAction;
    private bool loggedHapticFailure;
    private SteamVR_Behaviour_Pose rightHandPose;

    private void Awake()
    {
        ResolveHapticAction();
        rightHandPose = GetComponent<SteamVR_Behaviour_Pose>();
    }

    private void Update()
    {
        if (enableLocalKeyboardTest &&
            Input.GetKeyDown(localTestKey))
        {
            Debug.Log(
                "[ClapHaptic] Local keyboard test requested.",
                this
            );
            PlayNetworkClapHaptic();
        }
    }

    private void OnValidate()
    {
        hapticDuration = Mathf.Max(0.01f, hapticDuration);
        hapticFrequency = Mathf.Max(1f, hapticFrequency);
        hapticAmplitude = Mathf.Clamp01(hapticAmplitude);
        legacyPulseMicroseconds =
            Mathf.Clamp(legacyPulseMicroseconds, 100, 3999);
    }

    [ContextMenu("Test Right-Hand Haptic")]
    private void TestRightHandHaptic()
    {
        Debug.Log(
            "[ClapHaptic] Inspector test requested.",
            this
        );
        PlayNetworkClapHaptic();
    }

    /// <summary>
    /// Called only after the Actor Host confirms that the local actor hand
    /// and the synchronized audience right hand share the claphand volume.
    /// </summary>
    public void PlayNetworkClapHaptic()
    {
        if (hapticAction == null)
            ResolveHapticAction();

        bool legacyPulsePlayed = false;

        if (hapticAction == null)
        {
            legacyPulsePlayed = TryPlayLegacyVivePulse();

            if (!loggedMissingHapticAction)
            {
                Debug.LogWarning(
                    "[ClapHaptic] SteamVR action " +
                    "NewSet/highFiveHaptic was not found. LegacyPulse=" +
                    legacyPulsePlayed + ".",
                    this
                );
                loggedMissingHapticAction = true;
            }

            return;
        }

        try
        {
            if (hapticAction.actionSet != null &&
                !hapticAction.actionSet.IsActive(hapticSource))
            {
                hapticAction.actionSet.Activate(hapticSource);
            }

            EVRInputError error = OpenVR.Input == null
                ? EVRInputError.NoSteam
                : OpenVR.Input.TriggerHapticVibrationAction(
                    hapticAction.handle,
                    0f,
                    hapticDuration,
                    hapticFrequency,
                    hapticAmplitude,
                    SteamVR_Input_Source.GetHandle(hapticSource)
                );

            if (useLegacyVivePulseFallback)
                legacyPulsePlayed = TryPlayLegacyVivePulse();

            if (error != EVRInputError.None && !legacyPulsePlayed)
            {
                Debug.LogWarning(
                    "[ClapHaptic] SteamVR haptic failed. Error=" +
                    error + ".",
                    this
                );
                return;
            }

            if (debugLog)
            {
                Debug.Log(
                    "[ClapHaptic] Audience right-controller haptic " +
                    "played. ActionResult=" + error +
                    ", LegacyPulse=" + legacyPulsePlayed + ".",
                    this
                );
            }
        }
        catch (System.Exception exception)
        {
            legacyPulsePlayed = TryPlayLegacyVivePulse();

            if (legacyPulsePlayed)
            {
                Debug.Log(
                    "[ClapHaptic] SteamVR Action threw an exception, " +
                    "but the legacy Vive pulse was sent. " +
                    exception.Message,
                    this
                );
                return;
            }

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

    private bool TryPlayLegacyVivePulse()
    {
        if (!useLegacyVivePulseFallback || OpenVR.System == null)
            return false;

        if (rightHandPose == null)
            rightHandPose = GetComponent<SteamVR_Behaviour_Pose>();

        if (rightHandPose == null)
            return false;

        int deviceIndex = rightHandPose.GetDeviceIndex();

        if (deviceIndex < 0)
            return false;

        OpenVR.System.TriggerHapticPulse(
            (uint)deviceIndex,
            0,
            (ushort)legacyPulseMicroseconds
        );
        return true;
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
