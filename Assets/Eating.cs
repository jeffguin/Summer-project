using UnityEngine;


public class Eating : MonoBehaviour
{
    [SerializeField] private UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor mouthSocketInteractor;
    [SerializeField] private AudioSource eatingSound;

    public void EatFood()
    {
      var currentFood = mouthSocketInteractor.interactablesHovered[0];
      
      eatingSound.transform.position = mouthSocketInteractor.transform.position;
      eatingSound.Play();

      Destroy(currentFood.transform.gameObject);
    }

}
