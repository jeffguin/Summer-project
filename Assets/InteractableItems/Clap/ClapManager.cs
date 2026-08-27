using Oculus.Haptics;
using UnityEngine;

public class ClapManager : MonoBehaviour
{
    [SerializeField] private AudioSource ClapSound;

    [SerializeField] HapticSource hapticSourceActor;
    [SerializeField] HapticSource hapticSourceViewer;

    private void OnTriggerEnter(Collider other)
    {
        if(other.tag == "ClapSender")
        {
            Debug.Log("Clap detected!");
            if (!ClapSound.isPlaying)
            {
                ClapSound.Play();
                hapticSourceActor.Play(Controller.Both);
                hapticSourceViewer.Play(Controller.Both);
            }
        }
    }
}
