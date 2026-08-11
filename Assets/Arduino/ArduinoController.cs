using UnityEngine;
using System;
using System.IO.Ports;
using System.Threading;

public class ArduinoController : MonoBehaviour
{
    [Header("Arduino Serial Settings")]
    public string portName = "COM4";
    public int baudRate = 9600;

    private SerialPort serialPort;


    [Header("TicTacToe Object Spawning")]
    [SerializeField] private GameObject itemIntoVirtualPrefab;
    [SerializeField] private Transform spawnPoint;

    private GameObject spawnedItem;


    [Header("Sweet/Medal Detection, Destroy and Play Animation")]
    [SerializeField] private Collider TriggerCollider;
    [SerializeField] private Animator discAnimator;


    [Header("Arduino Messages")]
    [SerializeField] private string buttonPressMessage = "BUTTON_PRESS";
    [SerializeField] private string rotateCompleteMessage = "ROTATE_COMPLETE";
    [SerializeField] private string sweetCollectedMessage = "SWEET_COLLECTED";


    [Header("Animation")]
    [SerializeField] private string fallTriggerName = "Fall";


    [Header("Rotation")]
    public bool canRotate = false;


    private void Start()
    {
        try
        {
            serialPort = new SerialPort(portName, baudRate);
            serialPort.ReadTimeout = 50;
            serialPort.Open();

            Debug.Log("Arduino connected on " + portName);
        }
        catch (Exception e)
        {
            Debug.LogError("Could not connect to Arduino: " + e.Message);
        }
    }


    private void Update()
    {
        if (serialPort == null || !serialPort.IsOpen)
            return;

        try
        {
            while (serialPort.BytesToRead > 0)
            {
                string message = serialPort.ReadLine().Trim();

                Debug.Log("Arduino received: " + message);


                if (message == buttonPressMessage)
                {
                    ArduinoButtonPressed();
                }


                else if (message == rotateCompleteMessage)
                {
                    canRotate = true;

                    Debug.Log("Arduino rotation complete. canRotate = true");
                }
            }
        }
        catch (TimeoutException)
        {
            // Normal because ReadTimeout is set to 50 ms.
        }
        catch (Exception e)
        {
            Debug.LogError("Arduino read error: " + e.Message);
        }
    }


    public void ArduinoButtonPressed()
    {
        Debug.Log("Arduino button pressed.");

        SpawnItem();
    }


    private void SpawnItem()
    {
        if (itemIntoVirtualPrefab == null)
        {
            Debug.LogError("Item Into Virtual Prefab has not been assigned.");
            return;
        }


        if (spawnedItem != null)
        {
            Debug.LogWarning("An Item_Into_Virtual is already spawned.");
            return;
        }


        Vector3 spawnPosition = transform.position;
        Quaternion spawnRotation = transform.rotation;


        if (spawnPoint != null)
        {
            spawnPosition = spawnPoint.position;
            spawnRotation = spawnPoint.rotation;
        }


        spawnedItem = Instantiate(
            itemIntoVirtualPrefab,
            spawnPosition,
            spawnRotation
        );


        Debug.Log("Item_Into_Virtual spawned.");
    }


    private void OnTriggerEnter(Collider other)
    {
   
        if (!other.CompareTag("Sweet"))
            return;


        Debug.Log("Sweet object entered the trigger.");


        // Tell Arduino that the Sweet/medal was collected.
        SendToArduino(sweetCollectedMessage);

        PlayFallingAnimation();


        Destroy(other.gameObject);


        // Clear reference if this was the spawned object.
        if (other.gameObject == spawnedItem)
        {
            spawnedItem = null;
        }
    }


    private void PlayFallingAnimation()
    {
        if (discAnimator == null)
        {
            Debug.LogError("Disc Animator has not been assigned.");
            return;
        }


        discAnimator.SetTrigger(fallTriggerName);

        Debug.Log("FallingDisc animation triggered.");
    }


    public void SendToArduino(string message)
    {
        if (serialPort == null || !serialPort.IsOpen)
        {
            Debug.LogWarning(
                "Arduino is not connected. Could not send: " + message
            );

            return;
        }


        try
        {
            serialPort.WriteLine(message);

            Debug.Log("Sent to Arduino: " + message);
        }
        catch (Exception e)
        {
            Debug.LogError("Arduino write error: " + e.Message);
        }
    }


    private void OnApplicationQuit()
    {
        CloseSerialPort();
    }


    private void OnDestroy()
    {
        CloseSerialPort();
    }


    private void CloseSerialPort()
    {
        if (serialPort == null)
            return;


        try
        {
            if (serialPort.IsOpen)
            {
                serialPort.Close();

                Debug.Log("Arduino serial port closed.");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning(
                "Error closing Arduino serial port: " + e.Message
            );
        }
    }
}