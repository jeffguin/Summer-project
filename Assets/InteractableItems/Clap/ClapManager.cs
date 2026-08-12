using Oculus.Haptics;
using UnityEngine;

public class ClapManager : MonoBehaviour
{
    [SerializeField] private AudioSource ClapSound;

    [SerializeField] HapticSource hapticSource;
 
    private void OnTriggerEnter(Collider other)
    {
        if(other.CompareTag("ClapReceiver"))
        {
            Debug.Log("Clap detected!");
            if (!ClapSound.isPlaying)
            {
                ClapSound.Play();
                hapticSource.Play(Controller.Both);
            }
        }
    }
}
