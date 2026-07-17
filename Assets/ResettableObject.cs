using UnityEngine;


public class ResettableObject : MonoBehaviour
{

    private Vector3 startPosition;
    private Quaternion startRotation;
    private Vector3 startScale;



    void Awake()
    {
        StoreInitialState();
    }



    void StoreInitialState()
    {
        startPosition = transform.localPosition;
        startRotation = transform.localRotation;
        startScale = transform.localScale;
    }



    public void ResetObject()
    {
        transform.localPosition = startPosition;
        transform.localRotation = startRotation;
        transform.localScale = startScale;


        Rigidbody rb = GetComponent<Rigidbody>();

        if(rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }


        // Reset animation if present
        Animator animator = GetComponent<Animator>();

        if(animator != null)
        {
            animator.Rebind();
            animator.Update(0);
        }
    }
}