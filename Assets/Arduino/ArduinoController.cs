using UnityEngine;
using System.IO.Ports;

public class ArduinoController : MonoBehaviour
{
    SerialPort serial = new SerialPort("COM4", 9600);

    [SerializeField] private GameObject sweetPrefab;
    [SerializeField] private Transform spawnPoint;

    void Start()
    {
        serial.Open();
        serial.ReadTimeout = 50;
    }


    void Update()
    {
        // Unity > Arduino

        if (Input.GetKeyDown(KeyCode.Space))
        {
            serial.Write("1");
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
            }
            catch
            {

            }
        }
    }


    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Sweet"))
        {
            serial.Write("1");
            Destroy(other.gameObject);
        }
    }

    void ArduinoButtonPressed()
    {
        Debug.Log("Arduino button pressed!");
        Instantiate(sweetPrefab, spawnPoint.position, Quaternion.identity);
    }


    void OnApplicationQuit()
    {
        if (serial.IsOpen)
            serial.Close();
    }
}