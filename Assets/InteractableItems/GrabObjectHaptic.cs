using UnityEngine;

public class GrabObjectHaptic : MonoBehaviour
{
    private PlayHaptic haptic;

    void Start()
    {
        haptic = FindObjectOfType<PlayHaptic>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("GrabObject"))
        {
            haptic.PlayRightHand();
        }
    }
}