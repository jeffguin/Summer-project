using Oculus.Haptics;
using UnityEngine;
using System.Collections;

public class FloorTrigger : MonoBehaviour
{
    [SerializeField] private GameObject Player;
    [SerializeField] private GameObject Speakers;
    [SerializeField] HapticSource hapticSource;

    bool Vibrate = false;
    float waitTime = 0.5f;

    private void Update()
    {
        //if(Vibrate)
        //{
        //    hapticSource.Play(Controller.Both);
        //}
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.tag == "Player")
        {
            Speakers.SetActive(true);
            hapticSource.Play(Controller.Both);
            Vibrate = true;
            StartCoroutine(vibrateCoroutine());
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.tag == "Player")
        {
            Speakers.SetActive(false);
            Vibrate = false;
        }
    }

    IEnumerator vibrateCoroutine()
    {
        hapticSource.Play(Controller.Both);
        //yield on a new YieldInstruction that waits for 5 seconds.
        yield return new WaitForSeconds(waitTime);

        if (Vibrate)
        {
            StartCoroutine(vibrateCoroutine());
        }
    }
}