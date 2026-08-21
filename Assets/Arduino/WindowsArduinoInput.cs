using UnityEngine;
using System;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.IO.Ports;
#endif

public class WindowsArduinoInput : MonoBehaviour
{
    [SerializeField] private string portName = "COM4";
    [SerializeField] private int baudRate = 9600;

    [SerializeField] private string buttonPressMessage = "BUTTON_PRESS";
    [SerializeField] private string sweetCollectedMessage = "2";

    private WindowsToHeadsetSpawnBridge spawnBridge;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private SerialPort serialPort;
#endif


    private void Start()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        OpenSerialPort();
#endif
    }


    private void Update()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
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
                    Debug.Log("Physical Arduino button pressed.");

                    if (spawnBridge == null)
                    {
                        spawnBridge =
                            FindFirstObjectByType<WindowsToHeadsetSpawnBridge>();
                    }

                    if (spawnBridge == null)
                    {
                        Debug.LogError("Could not find WindowsToHeadsetSpawnBridge.");
                        return;
                    }

                    spawnBridge.RequestSpawn();
                }
            }
        }
        catch (TimeoutException)
        {
        }
        catch (Exception e)
        {
            Debug.LogError("Arduino read error: " + e.Message);
        }
#endif
    }


    public void SendSweetCollectedCommand()
    {
        SendToArduino(sweetCollectedMessage);
    }


    public void SendToArduino(string message)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        Debug.Log("SendToArduino called with: " + message);

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
#endif
    }


    private void OpenSerialPort()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (serialPort != null && serialPort.IsOpen)
            return;

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
#endif
    }


    private void OnApplicationQuit()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (serialPort != null && serialPort.IsOpen)
        {
            serialPort.Close();
        }
#endif
    }
}