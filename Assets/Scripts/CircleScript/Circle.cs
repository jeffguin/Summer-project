using UnityEngine;

public class Circle : MonoBehaviour
{
    [SerializeField] private float rotationSpeed = 5f;

    private void Update()
    {
        transform.Rotate(new Vector3 (0, rotationSpeed, 0) * Time.deltaTime);
    }
}
