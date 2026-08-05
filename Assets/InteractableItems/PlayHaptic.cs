using UnityEngine;
using Oculus.Haptics;

public class PlayHaptic : MonoBehaviour
{
    public HapticClip hapticClip;

    private HapticClipPlayer player;

    void Start()
    {
        player = new HapticClipPlayer(hapticClip);
    }

    public void PlayRightHand()
    {
        //player.Play(HapticInstance.Hand.Right);
    }

    public void PlayLeftHand()
    {
        //player.Play(HapticInstance.Hand.Left);
    }
}