using UnityEngine;
using UnityEngine.Audio;

public class drawerShifter : MonoBehaviour
{
    [Header("Movement Settings")]
    public float maxSpeed = 3f;
    public float volumeSmoothTime = 0.1f;

    [Header("Audio Settings")]
    public float maxVolume = 1f;
    public float minVolume = 0f;
    public bool autoPlay = true;

    private AudioSource audioSource;
    private Vector3 lastPosition;
    private float currentVelocity;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        lastPosition = transform.position;

        if (autoPlay && !audioSource.isPlaying)
        {
            audioSource.loop = true;
            audioSource.Play();
        }
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        float distance = Vector3.Distance(transform.position, lastPosition);
        float speed = distance / dt;
        lastPosition = transform.position;

        float targetVolume = Mathf.InverseLerp(0f, maxSpeed, speed);
        targetVolume = Mathf.Lerp(minVolume, maxVolume, targetVolume);

        audioSource.volume = Mathf.SmoothDamp(
            audioSource.volume,
            targetVolume,
            ref currentVelocity,
            volumeSmoothTime
        );
    }

}

