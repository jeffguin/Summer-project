using UnityEngine;
using UnityEngine.InputSystem; // Add this line!

public class EyeInput : MonoBehaviour
{
    public float speed = 1.0f;

    void Update()
    {
        Vector3 move = Vector3.zero;
        var keyboard = Keyboard.current;

        if (keyboard == null) return;

        // A/D Keys (Left/Right)
        if (keyboard.dKey.isPressed) move.x += 1;
        if (keyboard.aKey.isPressed) move.x -= 1;

        // W/S Keys (Up/Down)
        if (keyboard.wKey.isPressed) move.y += 1;
        if (keyboard.sKey.isPressed) move.y -= 1;

        // Q/E Keys (Forward/Back)
        if (keyboard.eKey.isPressed) move.z += 1;
        if (keyboard.qKey.isPressed) move.z -= 1;

        // Apply movement
        transform.Translate(move * speed * Time.deltaTime);
    }
}
