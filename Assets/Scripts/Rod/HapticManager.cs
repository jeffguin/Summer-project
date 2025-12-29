using Oculus.Haptics;
using UnityEngine;

public class HapticManager : MonoBehaviour
{
    [SerializeField] HapticSource hapticSource;
    [SerializeField] GameObject item;

    private void OnCollisionEnter(Collision collision)
    {
        if(collision.gameObject.tag == "Leaf" || collision.gameObject.tag == "Rod")
        {
            hapticSource.Play();
        }
    }
}
