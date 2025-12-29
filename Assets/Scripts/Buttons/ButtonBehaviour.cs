//using SteamAudio;
using UnityEngine;

public class ButtonBehaviour : MonoBehaviour
{
    [SerializeField] GameObject button;
    [SerializeField] GameObject soundScource;
    [SerializeField] Material offMaterial;
    [SerializeField] Material onMaterial;

    bool isActive = false;

    public void OnButtonClick()
    {
        if(isActive)
        {
            soundScource.SetActive(false);
            isActive = false;
            button.GetComponent<MeshRenderer>().material = offMaterial;
        }
        else if (!isActive)
        {
            soundScource.SetActive(true);
            isActive = true;
            button.GetComponent<MeshRenderer>().material = onMaterial;
        }
    }
}
