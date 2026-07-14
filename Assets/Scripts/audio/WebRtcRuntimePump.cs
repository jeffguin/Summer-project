using System.Collections;
using Unity.WebRTC;
using UnityEngine;

[DefaultExecutionOrder(-10000)]
public sealed class WebRtcRuntimePump : MonoBehaviour
{
    private static WebRtcRuntimePump instance;
    private Coroutine updateCoroutine;

    public static void EnsureExists()
    {
        if (instance != null)
            return;

        instance = FindFirstObjectByType<WebRtcRuntimePump>(FindObjectsInactive.Include);

        if (instance != null)
            return;

        GameObject runtimeObject = new GameObject("WebRTC Runtime Pump");
        instance = runtimeObject.AddComponent<WebRtcRuntimePump>();
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
        updateCoroutine = StartCoroutine(WebRTC.Update());
    }

    private void OnDestroy()
    {
        if (instance != this)
            return;

        if (updateCoroutine != null)
        {
            StopCoroutine(updateCoroutine);
            updateCoroutine = null;
        }

        instance = null;
    }
}
