using UnityEngine;
using System.IO.Ports;

public class ArduinoController : MonoBehaviour
{
    SerialPort serial = new SerialPort("COM4", 9600);

    [SerializeField] private GameObject Item_Into_Virtual;
    [SerializeField] private Transform spawnPoint;

    private bool canRotate = true;

    void Start()
    {
        serial.Open();
        serial.ReadTimeout = 50;
    }

    void Update()
    {

        // Unity > Arduino


        // Press Q to simulate Arduino button
        if (Input.GetKeyDown(KeyCode.Q))
        {
            ArduinoButtonPressed();
        }

        if (Input.GetKeyDown(KeyCode.W))
        {
            serial.Write("2");
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            serial.Write("3");
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            serial.Write("4");
        }

        // Arduino > Unity

        if (serial.IsOpen)
        {
            try
            {
                string message = serial.ReadLine();

                if (message == "BUTTON_PRESS")
                {
                    ArduinoButtonPressed();
                }
                else if (message == "ROTATE_COMPLETE")
                {
                    canRotate = true;
                }
            }
            catch
            {
                // Ignore timeout exceptions
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Trigger when any object tagged "Sweet" enters the trigger
        if (other.CompareTag("Sweet"))
        {
            if (serial.IsOpen)
            {
                serial.Write("2");
            }

            Destroy(other.gameObject);
        }
    }

    void ArduinoButtonPressed()
    {
        if (!canRotate)
            return;

        Debug.Log("Arduino button pressed!");

        Instantiate(
            Item_Into_Virtual,
            spawnPoint.position,
            Item_Into_Virtual.transform.rotation
        );

        canRotate = false;
    }

    void OnApplicationQuit()
    {
        if (serial.IsOpen)
        {
            serial.Close();
        }
    }
}