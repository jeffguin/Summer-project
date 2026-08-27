using Oculus.Haptics;
using UnityEngine;

public class HapticManager : MonoBehaviour
{
    [SerializeField] HapticSource hapticSourceActor;
    [SerializeField] HapticSource hapticSourceViewer;
    [SerializeField] GameObject item;

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.tag == "Leaf" || collision.gameObject.tag == "Rod")
        {
            hapticSourceViewer.Play();
        }
    }

    public void PlayHapticFeedback()
    {

        hapticSourceActor.Play();
        hapticSourceViewer.Play();
    }
}
