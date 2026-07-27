using UnityEngine;
using System.IO.Ports;

public class ArduinoController : MonoBehaviour
{
    SerialPort serial = new SerialPort("COM4", 9600);

    [SerializeField] private GameObject sweetPrefab;
    [SerializeField] private Transform spawnPoint;

    bool canRotate = true;
    void Start()
    {
        serial.Open();
        serial.ReadTimeout = 50;
    }

    void Update()
    {
        // Unity > Arduino

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

                else if(message == "ROTATE_COMPLETE")
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
        if (other.CompareTag("Sweet"))
        {
            serial.Write("2");
            Destroy(other.gameObject);
        }
    }

    void ArduinoButtonPressed()
    {
        if(canRotate)
        {
            Debug.Log("Arduino button pressed!");

            Instantiate(sweetPrefab, spawnPoint.position, sweetPrefab.transform.rotation);

            canRotate = false;
        }   
    }

    void OnApplicationQuit()
    {
        if (serial.IsOpen)
        {
            serial.Close();
        }
    }
}