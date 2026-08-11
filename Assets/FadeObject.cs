using UnityEngine;
using System.Collections;

public class FadeObject : MonoBehaviour
{
    [Header("Fade Settings")]
    public Renderer[] renderers;

    public float fadeDuration = 0.5f;

    private Coroutine fadeRoutine;

    public void FadeIn()
    {
        StartFade(1f);
    }

    public void FadeOut()
    {
        StartFade(0f);
    }

    private void StartFade(float targetAlpha)
    {
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
        }

        fadeRoutine = StartCoroutine(
            FadeRoutine(targetAlpha)
        );
    }

    private IEnumerator FadeRoutine(float targetAlpha)
    {
        // Safety check
        if (renderers == null || renderers.Length == 0)
        {
            Debug.LogWarning(
                $"FadeObject on '{gameObject.name}' has no Renderers assigned."
            );

            fadeRoutine = null;
            yield break;
        }

        // Find the first valid renderer
        Renderer firstRenderer = null;

        foreach (Renderer renderer in renderers)
        {
            if (renderer != null)
            {
                firstRenderer = renderer;
                break;
            }
        }

        if (firstRenderer == null)
        {
            Debug.LogWarning(
                $"FadeObject on '{gameObject.name}' has no valid Renderers."
            );

            fadeRoutine = null;
            yield break;
        }

        float currentAlpha =
            firstRenderer.material.color.a;

        float time = 0f;

        // Handle instant fade
        if (fadeDuration <= 0f)
        {
            SetAlpha(targetAlpha);
            fadeRoutine = null;
            yield break;
        }

        while (time < fadeDuration)
        {
            time += Time.deltaTime;

            float progress =
                Mathf.Clamp01(time / fadeDuration);

            float alpha =
                Mathf.Lerp(
                    currentAlpha,
                    targetAlpha,
                    progress
                );

            SetAlpha(alpha);

            yield return null;
        }

        // Make  sure the final alpha is exactly the requested value
        SetAlpha(targetAlpha);

        fadeRoutine = null;
    }

    private void SetAlpha(float alpha)
    {
        if (renderers == null)
            return;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            Color color = renderer.material.color;
            color.a = alpha;
            renderer.material.color = color;
        }
    }
}