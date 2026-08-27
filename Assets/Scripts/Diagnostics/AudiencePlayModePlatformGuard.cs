#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class AudiencePlayModePlatformGuard
{
    private const string AudienceSceneName = "Main_Audience_Windows";
    private const string InitialBuildTargetKey =
        "SummerProject.Rendering.InitialBuildTarget";
    private const string RestartRequiredKey =
        "SummerProject.Rendering.RestartRequired";

    private static bool dialogScheduled;

    static AudiencePlayModePlatformGuard()
    {
        string currentTarget =
            EditorUserBuildSettings.activeBuildTarget.ToString();
        string initialTarget = SessionState.GetString(
            InitialBuildTargetKey,
            string.Empty
        );

        if (string.IsNullOrEmpty(initialTarget))
        {
            SessionState.SetString(InitialBuildTargetKey, currentTarget);
        }
        else if (!string.Equals(
                     initialTarget,
                     currentTarget,
                     StringComparison.Ordinal))
        {
            SessionState.SetBool(RestartRequiredKey, true);
        }

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        Application.logMessageReceived -= OnUnityLog;
        Application.logMessageReceived += OnUnityLog;
    }

    private static void OnPlayModeStateChanged(
        PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode ||
            !IsAudienceSceneActive())
        {
            return;
        }

        if (EditorUserBuildSettings.activeBuildTarget !=
            BuildTarget.StandaloneWindows64)
        {
            StopPlayAndExplain(
                "观众端场景必须在 StandaloneWindows64 Build Target 下运行。" +
                "当前 Target 是 " +
                EditorUserBuildSettings.activeBuildTarget +
                "。请先在 File > Build Profiles 中切换到 Windows，" +
                "然后重启 Unity。"
            );
            return;
        }

        if (SessionState.GetBool(RestartRequiredKey, false))
        {
            StopPlayAndExplain(
                "本次 Unity 会话中切换过 Build Target，URP 的全局数组" +
                "仍可能保留 Android 的 32 项容量。请重启 Unity 后再运行" +
                "观众端；重启是 Unity 对 UUM-92830 给出的官方规避方式。"
            );
        }
    }

    private static void OnUnityLog(
        string condition,
        string stackTrace,
        LogType type)
    {
        if (type != LogType.Warning ||
            string.IsNullOrEmpty(condition) ||
            !IsUrpArrayCapacityWarning(condition))
        {
            return;
        }

        SessionState.SetBool(RestartRequiredKey, true);

        if (EditorApplication.isPlaying && IsAudienceSceneActive())
        {
            StopPlayAndExplain(
                "检测到 URP 全局数组容量冲突，已立即停止观众端，" +
                "避免每帧数百条警告最终拖死 Editor。请重启 Unity，" +
                "并确认 Build Target 为 StandaloneWindows64。"
            );
        }
    }

    private static bool IsAudienceSceneActive()
    {
        Scene scene = SceneManager.GetActiveScene();
        return scene.IsValid() &&
               string.Equals(
                   scene.name,
                   AudienceSceneName,
                   StringComparison.Ordinal
               );
    }

    private static bool IsUrpArrayCapacityWarning(string condition)
    {
        if (condition.IndexOf(
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

    private static void StopPlayAndExplain(string reason)
    {
        EditorApplication.isPlaying = false;

        if (dialogScheduled)
            return;

        dialogScheduled = true;
        Debug.LogError("Audience rendering safety guard: " + reason);

        EditorApplication.delayCall += () =>
        {
            dialogScheduled = false;
            EditorUtility.DisplayDialog(
                "观众端渲染配置需要处理",
                reason,
                "确定"
            );
        };
    }
}
#endif
