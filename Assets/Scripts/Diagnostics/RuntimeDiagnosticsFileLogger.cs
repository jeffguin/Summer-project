using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Fusion;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-10000)]
public sealed class RuntimeDiagnosticsFileLogger : MonoBehaviour
{
    private const float HeartbeatIntervalSeconds = 5f;
    private const int MaximumCapturedTextLength = 2048;
    private const double DuplicateWarningWindowSeconds = 5d;
    private const double CriticalStageTimeoutSeconds = 0.5d;
    private const int CriticalStagePollMilliseconds = 100;

    private static RuntimeDiagnosticsFileLogger instance;

    private readonly object writerLock = new object();
    private readonly object warningRateLimitLock = new object();
    private readonly object criticalStageLock = new object();
    private readonly Dictionary<string, WarningRateState> warningRateStates =
        new Dictionary<string, WarningRateState>();
    private StreamWriter writer;
    private System.Threading.Timer criticalStageTimer;
    private string logPath;
    private float heartbeatElapsed;
    private float frameElapsed;
    private int framesSinceHeartbeat;
    private bool shuttingDown;
    private bool urpArrayConflictReported;

    private string criticalStage;
    private long criticalStageStartedAt;
    private int criticalStageToken;
    private int reportedCriticalStageToken;

    private sealed class WarningRateState
    {
        public DateTime LastWrittenUtc;
        public int SuppressedCount;
    }

    public static string LogPath => instance != null
        ? instance.logPath
        : string.Empty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
            return;

        var loggerObject = new GameObject("Runtime Diagnostics File Logger");
        DontDestroyOnLoad(loggerObject);
        instance = loggerObject.AddComponent<RuntimeDiagnosticsFileLogger>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        try
        {
#if UNITY_EDITOR
            string logDirectory = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Logs")
            );
#else
            string logDirectory = Path.Combine(
                Application.persistentDataPath,
                "Diagnostics"
            );
#endif
            Directory.CreateDirectory(logDirectory);
            logPath = Path.Combine(
                logDirectory,
                "AvatarRuntimeDiagnostics.log"
            );

            writer = new StreamWriter(
                logPath,
                false,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            )
            {
                AutoFlush = true
            };

            Application.logMessageReceivedThreaded += HandleUnityLog;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            criticalStageTimer = new System.Threading.Timer(
                CheckCriticalStage,
                null,
                CriticalStagePollMilliseconds,
                CriticalStagePollMilliseconds
            );

            WriteLine(
                $"START utc={DateTime.UtcNow:O} " +
                $"platform={Application.platform} " +
                $"buildTarget={GetBuildTargetName()} " +
                $"graphicsApi={SystemInfo.graphicsDeviceType} " +
                $"quality={GetQualityName()} " +
                $"renderPipeline={GetRenderPipelineName()} " +
                $"unity={Application.unityVersion} " +
                $"product={Application.productName}"
            );

            Debug.Log(
                "RuntimeDiagnosticsFileLogger: Writing diagnostics to " +
                logPath
            );
        }
        catch (Exception exception)
        {
            writer = null;
            Debug.LogWarning(
                "RuntimeDiagnosticsFileLogger could not open its file. " +
                exception.Message
            );
        }
    }

    private void Update()
    {
        float deltaTime = Mathf.Max(0f, Time.unscaledDeltaTime);
        heartbeatElapsed += deltaTime;
        frameElapsed += deltaTime;
        framesSinceHeartbeat++;

        if (heartbeatElapsed < HeartbeatIntervalSeconds)
            return;

        float framesPerSecond = frameElapsed > 0f
            ? framesSinceHeartbeat / frameElapsed
            : 0f;

        long totalAllocatedMemory = Profiler.GetTotalAllocatedMemoryLong();
        long monoUsedMemory = Profiler.GetMonoUsedSizeLong();
        int runnerCount = FindObjectsByType<NetworkRunner>(
            FindObjectsSortMode.None
        ).Length;

        var scenes = new StringBuilder();

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (i > 0)
                scenes.Append('|');

            Scene scene = SceneManager.GetSceneAt(i);
            scenes.Append(scene.name);
            scenes.Append(':');
            scenes.Append(scene.buildIndex);
        }

        WriteLine(
            $"HEARTBEAT utc={DateTime.UtcNow:O} " +
            $"frame={Time.frameCount} fps={framesPerSecond:F1} " +
            $"allocatedMB={totalAllocatedMemory / (1024f * 1024f):F1} " +
            $"monoMB={monoUsedMemory / (1024f * 1024f):F1} " +
            $"runners={runnerCount} scenes={scenes}"
        );

        heartbeatElapsed = 0f;
        frameElapsed = 0f;
        framesSinceHeartbeat = 0;
    }

    private void HandleUnityLog(
        string condition,
        string stackTrace,
        UnityEngine.LogType type)
    {
        if (type == UnityEngine.LogType.Warning &&
            !urpArrayConflictReported &&
            IsUrpArrayCapacityWarning(condition))
        {
            urpArrayConflictReported = true;
            WriteLine(
                $"RENDER_PIPELINE_ARRAY_CONFLICT " +
                $"utc={DateTime.UtcNow:O} " +
                "restartEditorRequired=true " +
                "cause=build_target_array_capacity_mismatch"
            );
        }

        bool isLifecycleLog =
            type == UnityEngine.LogType.Log &&
            condition != null &&
            (condition.StartsWith(
                 "BasicSpawner:",
                 StringComparison.Ordinal
             ) ||
             condition.StartsWith(
                 "ActorMovementNetworkHandler:",
                 StringComparison.Ordinal
             ));

        if (type == UnityEngine.LogType.Log && !isLifecycleLog)
            return;

        int suppressedDuplicates = 0;

        if (type == UnityEngine.LogType.Warning &&
            ShouldSuppressDuplicateWarning(
                condition,
                out suppressedDuplicates))
        {
            return;
        }

        string safeCondition = LimitAndFlatten(condition);
        string safeStackTrace = LimitAndFlatten(stackTrace);

        WriteLine(
            $"UNITY_{type.ToString().ToUpperInvariant()} " +
            $"utc={DateTime.UtcNow:O} message={safeCondition} " +
            $"suppressedDuplicates={suppressedDuplicates} " +
            $"stack={safeStackTrace}"
        );
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Camera[] cameras = FindObjectsByType<Camera>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );
        Light[] lights = FindObjectsByType<Light>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        int sceneCameraCount = 0;
        int activeCameraCount = 0;
        int sceneLightCount = 0;
        var cameraSummary = new StringBuilder();

        foreach (Camera camera in cameras)
        {
            if (camera.gameObject.scene != scene)
                continue;

            sceneCameraCount++;
            bool isRendering =
                camera.enabled && camera.gameObject.activeInHierarchy;

            if (isRendering)
                activeCameraCount++;

            if (cameraSummary.Length > 0)
                cameraSummary.Append('|');

            cameraSummary.Append(LimitAndFlatten(camera.name));
            cameraSummary.Append(':');
            cameraSummary.Append(isRendering ? "rendering" : "inactive");
            cameraSummary.Append(':');
            cameraSummary.Append(camera.stereoTargetEye);
            cameraSummary.Append(':');
            cameraSummary.Append(
                camera.targetTexture != null
                    ? LimitAndFlatten(camera.targetTexture.name)
                    : "display" + camera.targetDisplay
            );
        }

        foreach (Light light in lights)
        {
            if (light.gameObject.scene == scene)
                sceneLightCount++;
        }

        WriteLine(
            $"RENDER_CONFIG utc={DateTime.UtcNow:O} " +
            $"scene={scene.name} loadMode={mode} " +
            $"buildTarget={GetBuildTargetName()} " +
            $"graphicsApi={SystemInfo.graphicsDeviceType} " +
            $"quality={GetQualityName()} " +
            $"renderPipeline={GetRenderPipelineName()} " +
            $"cameras={activeCameraCount}/{sceneCameraCount} " +
            $"lights={sceneLightCount} cameraDetails={cameraSummary}"
        );
    }

    private static bool IsUrpArrayCapacityWarning(string condition)
    {
        if (string.IsNullOrEmpty(condition) ||
            condition.IndexOf(
                "exceeds previous array size",
                StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        return condition.IndexOf(
                   "urp_ReflProbes_",
                   StringComparison.Ordinal) >= 0 ||
               condition.IndexOf(
                   "_AdditionalShadowParams",
                   StringComparison.Ordinal) >= 0 ||
               condition.IndexOf(
                   "_AdditionalLights",
                   StringComparison.Ordinal) >= 0;
    }

    private static string GetBuildTargetName()
    {
#if UNITY_EDITOR
        return UnityEditor.EditorUserBuildSettings
            .activeBuildTarget
            .ToString();
#else
        return Application.platform.ToString();
#endif
    }

    private static string GetQualityName()
    {
        string[] qualityNames = QualitySettings.names;
        int qualityLevel = QualitySettings.GetQualityLevel();

        return qualityLevel >= 0 && qualityLevel < qualityNames.Length
            ? qualityNames[qualityLevel]
            : qualityLevel.ToString();
    }

    private static string GetRenderPipelineName()
    {
        RenderPipelineAsset renderPipeline =
            GraphicsSettings.currentRenderPipeline;

        return renderPipeline != null
            ? LimitAndFlatten(renderPipeline.name)
            : "BuiltIn";
    }

    public static int BeginCriticalStage(string stage)
    {
        RuntimeDiagnosticsFileLogger logger = instance;

        if (logger == null || string.IsNullOrEmpty(stage))
            return 0;

        return logger.BeginCriticalStageInternal(stage);
    }

    public static void EndCriticalStage(int token)
    {
        RuntimeDiagnosticsFileLogger logger = instance;

        if (logger == null || token == 0)
            return;

        logger.EndCriticalStageInternal(token);
    }

    private bool ShouldSuppressDuplicateWarning(
        string condition,
        out int suppressedDuplicates)
    {
        suppressedDuplicates = 0;

        if (string.IsNullOrEmpty(condition))
            return false;

        DateTime now = DateTime.UtcNow;

        lock (warningRateLimitLock)
        {
            if (!warningRateStates.TryGetValue(
                    condition,
                    out WarningRateState state))
            {
                warningRateStates[condition] = new WarningRateState
                {
                    LastWrittenUtc = now
                };
                return false;
            }

            if ((now - state.LastWrittenUtc).TotalSeconds <
                DuplicateWarningWindowSeconds)
            {
                state.SuppressedCount++;
                return true;
            }

            suppressedDuplicates = state.SuppressedCount;
            state.SuppressedCount = 0;
            state.LastWrittenUtc = now;
            return false;
        }
    }

    private int BeginCriticalStageInternal(string stage)
    {
        lock (criticalStageLock)
        {
            criticalStageToken++;

            if (criticalStageToken == 0)
                criticalStageToken++;

            criticalStage = stage;
            criticalStageStartedAt =
                System.Diagnostics.Stopwatch.GetTimestamp();
            reportedCriticalStageToken = 0;
            return criticalStageToken;
        }
    }

    private void EndCriticalStageInternal(int token)
    {
        lock (criticalStageLock)
        {
            if (token != criticalStageToken)
                return;

            criticalStage = null;
            criticalStageStartedAt = 0;
        }
    }

    private void CheckCriticalStage(object state)
    {
        string stalledStage = null;
        double elapsedMilliseconds = 0d;

        lock (criticalStageLock)
        {
            if (criticalStage == null ||
                criticalStageStartedAt == 0 ||
                reportedCriticalStageToken == criticalStageToken)
            {
                return;
            }

            long elapsedTicks =
                System.Diagnostics.Stopwatch.GetTimestamp() -
                criticalStageStartedAt;

            double elapsedSeconds =
                elapsedTicks /
                (double)System.Diagnostics.Stopwatch.Frequency;

            if (elapsedSeconds < CriticalStageTimeoutSeconds)
                return;

            reportedCriticalStageToken = criticalStageToken;
            stalledStage = criticalStage;
            elapsedMilliseconds = elapsedSeconds * 1000d;
        }

        WriteLine(
            $"MAIN_THREAD_STALL utc={DateTime.UtcNow:O} " +
            $"stage={stalledStage} " +
            $"durationMs={elapsedMilliseconds:F0}"
        );
    }

    private static string LimitAndFlatten(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        string flattened = value
            .Replace('\r', ' ')
            .Replace('\n', ' ');

        return flattened.Length <= MaximumCapturedTextLength
            ? flattened
            : flattened.Substring(0, MaximumCapturedTextLength);
    }

    private void WriteLine(string message)
    {
        lock (writerLock)
        {
            if (writer == null || shuttingDown)
                return;

            writer.WriteLine(message);
        }
    }

    private void OnApplicationQuit()
    {
        ShutdownWriter("APPLICATION_QUIT");
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;

        ShutdownWriter("LOGGER_DESTROYED");
    }

    private void ShutdownWriter(string reason)
    {
        criticalStageTimer?.Dispose();
        criticalStageTimer = null;

        lock (writerLock)
        {
            if (shuttingDown)
                return;

            if (writer != null)
            {
                writer.WriteLine(
                    $"STOP utc={DateTime.UtcNow:O} reason={reason}"
                );
                writer.Flush();
            }

            shuttingDown = true;
            Application.logMessageReceivedThreaded -= HandleUnityLog;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            writer?.Dispose();
            writer = null;
        }
    }
}
