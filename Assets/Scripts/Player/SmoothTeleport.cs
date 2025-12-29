
using UnityEngine;

public class SmoothTeleport : MonoBehaviour
{
    [SerializeField] GameObject player;

    public void movePlayer(Vector3 target)
    {
        player.transform.position = Vector3.MoveTowards(player.transform.position, target, 2 * Time.deltaTime);
    }
}
