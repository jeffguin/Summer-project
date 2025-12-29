using UnityEngine;

public class Footstep : MonoBehaviour
{
    [SerializeField] float rayDistance = 2f;
    [SerializeField] LayerMask groundLayer;
    [SerializeField] AudioSource FootstepAudio;

    private Vector3 lastPosition;
    public bool isMoving;

    private AudioClip currentClip;

    void Start()
    {
        lastPosition = transform.position;
    }

    void Update()
    {
        Ray ray = new Ray(transform.position, Vector3.down);
        RaycastHit hit;

        AudioClip newClip = null;

        // Raycast to detect floor type
        if (Physics.Raycast(ray, out hit, rayDistance, groundLayer))
        {
            FloorType floor = hit.collider.GetComponent<FloorType>();
            if (floor != null)
            {
                newClip = floor.footstepSound;
            }
        }

        // If the clip changed, update the AudioSource
        if (newClip != currentClip && newClip != null)
        {
            currentClip = newClip;
            FootstepAudio.clip = currentClip;
        }

        // Movement Detection
        float dist = (transform.position - lastPosition).sqrMagnitude;
        isMoving = dist > 0.0001f;
        lastPosition = transform.position;

        // Footstep playback
        if (isMoving)
        {
            if (!FootstepAudio.isPlaying && FootstepAudio.clip != null)
            {
                FootstepAudio.Play();
            }
        }
        else
        {
            if (FootstepAudio.isPlaying)
            {
                FootstepAudio.Stop();
            }
        }
    }
}