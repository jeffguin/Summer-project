using System;
using System.IO;
using System.Text;
using Fusion;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-10000)]
public sealed class RuntimeDiagnosticsFileLogger : MonoBehaviour
{
    private const float HeartbeatIntervalSeconds = 5f;
    private const int MaximumCapturedTextLength = 2048;

    private static RuntimeDiagnosticsFileLogger instance;

    private readonly object writerLock = new object();
    private StreamWriter writer;
    private string logPath;
    private float heartbeatElapsed;
    private float frameElapsed;
    private int framesSinceHeartbeat;
    private bool shuttingDown;

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
            WriteLine(
                $"START utc={DateTime.UtcNow:O} " +
                $"platform={Application.platform} " +
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
        bool isSpawnerLifecycleLog =
            type == UnityEngine.LogType.Log &&
            condition != null &&
            condition.StartsWith(
                "BasicSpawner:",
                StringComparison.Ordinal
            );

        if (type == UnityEngine.LogType.Log && !isSpawnerLifecycleLog)
            return;

        string safeCondition = LimitAndFlatten(condition);
        string safeStackTrace = LimitAndFlatten(stackTrace);

        WriteLine(
            $"UNITY_{type.ToString().ToUpperInvariant()} " +
            $"utc={DateTime.UtcNow:O} message={safeCondition} " +
            $"stack={safeStackTrace}"
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
            writer?.Dispose();
            writer = null;
        }
    }
}
