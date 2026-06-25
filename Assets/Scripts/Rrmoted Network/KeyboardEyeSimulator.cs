using UnityEngine;

public class KeyboardEyeSimulator : MonoBehaviour
{
    public float moveSpeed = 2.0f;

    private void Update()
    {
        Vector3 movement = Vector3.zero;

        if (Input.GetKey(KeyCode.A))
            movement.x -= 1f;

        if (Input.GetKey(KeyCode.D))
            movement.x += 1f;

        if (Input.GetKey(KeyCode.W))
            movement.z += 1f;

        if (Input.GetKey(KeyCode.S))
            movement.z -= 1f;

        if (Input.GetKey(KeyCode.E))
            movement.y += 1f;

        if (Input.GetKey(KeyCode.Q))
            movement.y -= 1f;

        transform.position += movement * moveSpeed * Time.deltaTime;
    }
}