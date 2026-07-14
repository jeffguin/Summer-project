using UnityEngine;
using System.Collections;


public class FadeObject : MonoBehaviour
{
    public Renderer[] renderers;

    public float fadeDuration = 0.5f;


    Coroutine fadeRoutine;



    public void FadeIn()
    {
        StartFade(1f);
    }



    public void FadeOut()
    {
        StartFade(0f);
    }



    void StartFade(float targetAlpha)
    {

        if(fadeRoutine != null)
            StopCoroutine(fadeRoutine);


        fadeRoutine = StartCoroutine(
            FadeRoutine(targetAlpha)
        );
    }



    IEnumerator FadeRoutine(float targetAlpha)
    {

        float currentAlpha =
            renderers[0].material.color.a;


        float time = 0;


        while(time < fadeDuration)
        {
            time += Time.deltaTime;


            float alpha =
                Mathf.Lerp(
                    currentAlpha,
                    targetAlpha,
                    time / fadeDuration
                );


            foreach(Renderer renderer in renderers)
            {
                Color color =
                    renderer.material.color;


                color.a = alpha;


                renderer.material.color = color;
            }


            yield return null;
        }
    }
}