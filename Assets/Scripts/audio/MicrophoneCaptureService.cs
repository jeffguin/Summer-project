using System;
using System.Collections;
using Unity.WebRTC;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

[DisallowMultipleComponent]
public sealed class MicrophoneCaptureService : MonoBehaviour
{
    public sealed class CaptureResult
    {
        public bool Success;
        public string ErrorCode;
        public string Message;
    }

    [Header("Audio Capture")]

    [SerializeField]
    private AudioSource captureAudioSource;

    [SerializeField]
    [Min(8000)]
    private int sampleRate = 48000;

    [SerializeField]
    [Min(1)]
    private int clipLengthSeconds = 1;

    /*
     * 不能把 SerializeField 字段放进：
     *
     * #if UNITY_ANDROID && !UNITY_EDITOR
     *
     * 否则 Editor 和 Android Player 看到的序列化字段布局不同，
     * 会导致：
     *
     * Script class layout is incompatible between the editor and the player.
     */
    [SerializeField]
    [Min(0.1f)]
    private float permissionTimeoutSeconds = 15f;

    [SerializeField]
    [Min(0.1f)]
    private float captureStartTimeoutSeconds = 5f;

    private string selectedDeviceName = string.Empty;
    private string activeDeviceName;

    private bool microphoneStarted;
    private AudioClip microphoneClip;

    public AudioStreamTrack Track { get; private set; }

    public bool IsCapturing =>
        microphoneStarted &&
        Track != null;

    public string SelectedDeviceName => selectedDeviceName;

    private void Awake()
    {
        EnsureAudioSource();
    }

    /// <summary>
    /// 获取当前平台可用的麦克风设备。
    /// </summary>
    public string[] GetDevices()
    {
        string[] devices = Microphone.devices;

        return devices == null
            ? Array.Empty<string>()
            : devices;
    }

    /// <summary>
    /// 设置要使用的麦克风设备。
    /// 传入空字符串表示使用系统默认麦克风。
    /// </summary>
    public bool SetSelectedDevice(
        string deviceName,
        out string error)
    {
        string requestedDevice = deviceName ?? string.Empty;

        if (!string.IsNullOrEmpty(requestedDevice))
        {
            bool exists = false;

            foreach (string device in GetDevices())
            {
                if (string.Equals(
                        device,
                        requestedDevice,
                        StringComparison.Ordinal))
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                error =
                    "Selected microphone is no longer available: " +
                    requestedDevice;

                return false;
            }
        }

        selectedDeviceName = requestedDevice;
        error = string.Empty;

        return true;
    }

    /// <summary>
    /// 启动麦克风采集，并创建 WebRTC AudioStreamTrack。
    /// </summary>
    public IEnumerator StartCapture(
        Action<CaptureResult> completed)
    {
        StopCapture();
        EnsureAudioSource();

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(
                Permission.Microphone))
        {
            bool permissionCompleted = false;
            bool permissionGranted = false;

            PermissionCallbacks callbacks =
                new PermissionCallbacks();

            callbacks.PermissionGranted += _ =>
            {
                permissionGranted = true;
                permissionCompleted = true;
            };

            callbacks.PermissionDenied += _ =>
            {
                permissionCompleted = true;
            };

            callbacks.PermissionDeniedAndDontAskAgain += _ =>
            {
                permissionCompleted = true;
            };

            Permission.RequestUserPermission(
                Permission.Microphone,
                callbacks);

            float permissionDeadline =
                Time.realtimeSinceStartup +
                Mathf.Max(0.1f, permissionTimeoutSeconds);

            while (!permissionCompleted &&
                   Time.realtimeSinceStartup < permissionDeadline)
            {
                yield return null;
            }

            bool currentlyAuthorized =
                Permission.HasUserAuthorizedPermission(
                    Permission.Microphone);

            if (!permissionGranted && !currentlyAuthorized)
            {
                Complete(
                    completed,
                    false,
                    "PermissionDenied",
                    "Microphone permission was not granted on Quest.");

                yield break;
            }
        }
#endif

        string validationError;

        if (!SetSelectedDevice(
                selectedDeviceName,
                out validationError))
        {
            selectedDeviceName = string.Empty;

            Debug.LogWarning(
                "MicrophoneCaptureService: " +
                validationError +
                ". Falling back to the default device.");
        }

        /*
         * 在 Microphone.Start 中传入 null，
         * 表示使用当前平台的默认麦克风。
         *
         * Android/Quest 有时不会返回明确的设备名称，
         * 但默认输入设备仍然可以正常工作。
         */
        activeDeviceName =
            string.IsNullOrEmpty(selectedDeviceName)
                ? null
                : selectedDeviceName;

        try
        {
            microphoneClip = Microphone.Start(
                activeDeviceName,
                true,
                Mathf.Max(1, clipLengthSeconds),
                Mathf.Max(8000, sampleRate));
        }
        catch (Exception exception)
        {
            Complete(
                completed,
                false,
                "MicrophoneStartException",
                exception.Message);

            yield break;
        }

        if (microphoneClip == null)
        {
            Complete(
                completed,
                false,
                "MicrophoneStartReturnedNull",
                "Microphone.Start returned a null AudioClip.");

            yield break;
        }

        microphoneStarted = true;

        float captureDeadline =
            Time.realtimeSinceStartup +
            Mathf.Max(0.1f, captureStartTimeoutSeconds);

        while (Microphone.GetPosition(activeDeviceName) <= 0 &&
               Time.realtimeSinceStartup < captureDeadline)
        {
            yield return null;
        }

        if (Microphone.GetPosition(activeDeviceName) <= 0)
        {
            StopCapture();

            Complete(
                completed,
                false,
                "MicrophoneNoSamples",
                "The microphone did not start producing samples.");

            yield break;
        }

        captureAudioSource.clip = microphoneClip;
        captureAudioSource.loop = true;
        captureAudioSource.Play();

        try
        {
            Track = new AudioStreamTrack(captureAudioSource)
            {
                Loopback = false
            };
        }
        catch (Exception exception)
        {
            StopCapture();

            Complete(
                completed,
                false,
                "AudioTrackCreationFailed",
                exception.Message);

            yield break;
        }

        Complete(
            completed,
            true,
            string.Empty,
            "Microphone capture is producing samples.");
    }

    /// <summary>
    /// 停止麦克风采集并释放 WebRTC 音频轨道。
    /// </summary>
    public void StopCapture()
    {
        if (Track != null)
        {
            Track.Dispose();
            Track = null;
        }

        if (captureAudioSource != null)
        {
            captureAudioSource.Stop();
            captureAudioSource.clip = null;
        }

        if (microphoneStarted)
        {
            try
            {
                Microphone.End(activeDeviceName);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "MicrophoneCaptureService: " +
                    "Failed to stop microphone cleanly. " +
                    exception.Message);
            }
        }

        microphoneStarted = false;
        activeDeviceName = null;
        microphoneClip = null;
    }

    /// <summary>
    /// 确保当前对象上存在用于采集的 AudioSource。
    /// </summary>
    private void EnsureAudioSource()
    {
        if (captureAudioSource == null)
        {
            captureAudioSource = GetComponent<AudioSource>();
        }

        if (captureAudioSource == null)
        {
            captureAudioSource =
                gameObject.AddComponent<AudioSource>();
        }

        captureAudioSource.playOnAwake = false;
        captureAudioSource.loop = true;
        captureAudioSource.spatialBlend = 0f;
        captureAudioSource.volume = 1f;
        captureAudioSource.mute = false;
    }

    private static void Complete(
        Action<CaptureResult> completed,
        bool success,
        string errorCode,
        string message)
    {
        completed?.Invoke(
            new CaptureResult
            {
                Success = success,
                ErrorCode = errorCode,
                Message = message
            });
    }

    private void OnDisable()
    {
        StopCapture();
    }

    private void OnDestroy()
    {
        StopCapture();
    }
}