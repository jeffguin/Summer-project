using Oculus.Haptics;
using UnityEngine;

public class BowPassThrough : MonoBehaviour
{
    [SerializeField] private GameObject Bow;
    [SerializeField] private AudioSource BowSound;

    [SerializeField] HapticSource hapticSource;

    bool Vibrate = false;

    private void Update()
    {

    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.tag == "Bow")
        {
            if (!BowSound.isPlaying)
            {
                BowSound.Play();
                hapticSource.Play(Controller.Both);
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.tag == "Bow")
        {
            //BowSound.Stop();
        }
    }
}
