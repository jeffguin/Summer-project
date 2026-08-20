using Fusion;
using UnityEngine;
using System;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.IO.Ports;
#endif

[RequireComponent(typeof(NetworkObject))]
public class WindowsToHeadsetArduino : NetworkBehaviour
{
    [Header("Arduino Serial Settings")]
    [SerializeField] private string portName = "COM4";
    [SerializeField] private int baudRate = 9600;

    [Header("Arduino Messages")]
    [SerializeField] private string buttonPressMessage = "BUTTON_PRESS";
    [SerializeField] private string sweetDropMessage = "2";

    [Header("Headset Network Controller")]
    [SerializeField] private ArduinoDropDiscNetworkSync headsetNetworkSync;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private SerialPort serialPort;
#endif


    public override void Spawned()
    {
        Debug.Log(
            $"[WindowsToHeadsetArduino] Network object spawned. " +
            $"HasStateAuthority={Object.HasStateAuthority}"
        );

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        OpenSerialPort();
#endif
    }


    private void Update()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

        if (Input.GetKeyDown(KeyCode.R))
        {
            Debug.Log(
                "[WindowsToHeadsetArduino] R pressed. Simulating Arduino button."
            );

            ArduinoButtonPressed();
        }


        if (Input.GetKeyDown(KeyCode.Space))
        {
            Debug.Log(
                "[WindowsToHeadsetArduino] Space pressed. Testing Arduino Sweet drop command."
            );

            SendToArduino(sweetDropMessage);
        }


        if (serialPort == null || !serialPort.IsOpen)
            return;


        try
        {
            while (serialPort.BytesToRead > 0)
            {
                string message = serialPort.ReadLine().Trim();

                Debug.Log(
                    "[WindowsToHeadsetArduino] Arduino received: " +
                    message
                );


                if (message == buttonPressMessage)
                {
                    Debug.Log(
                        "[WindowsToHeadsetArduino] Arduino button detected."
                    );

                    ArduinoButtonPressed();
                }
            }
        }
        catch (TimeoutException)
        {
        }
        catch (Exception e)
        {
            Debug.LogError(
                "[WindowsToHeadsetArduino] Arduino read error: " +
                e.Message
            );
        }

#endif
    }


    private void ArduinoButtonPressed()
    {
        if (Object == null || !Object.IsValid)
        {
            Debug.LogError(
                "[WindowsToHeadsetArduino] NetworkObject is not spawned."
            );

            return;
        }


        Debug.Log(
            "[WindowsToHeadsetArduino] Sending Arduino button press to headset."
        );


        if (Object.HasStateAuthority)
        {
            HandleButtonPressOnHeadset();
            return;
        }


        RPC_SendButtonPressToHeadset();
    }


    [Rpc(
        RpcSources.All,
        RpcTargets.StateAuthority,
        Channel = RpcChannel.Reliable)]
    private void RPC_SendButtonPressToHeadset(RpcInfo info = default)
    {
        Debug.Log(
            "[WindowsToHeadsetArduino] Headset received Arduino button press " +
            $"from {info.Source}."
        );

        HandleButtonPressOnHeadset();
    }


    private void HandleButtonPressOnHeadset()
    {
        if (!Object.HasStateAuthority)
        {
            Debug.LogWarning(
                "[WindowsToHeadsetArduino] Button handling attempted without State Authority."
            );

            return;
        }


        FindHeadsetNetworkSync();


        if (headsetNetworkSync == null)
        {
            Debug.LogError(
                "[WindowsToHeadsetArduino] Could not find ArduinoDropDiscNetworkSync on headset."
            );

            return;
        }


        Debug.Log(
            "[WindowsToHeadsetArduino] Requesting Sweet spawn on headset."
        );


        headsetNetworkSync.TrySpawnItemFromStateAuthority();
    }


    public void SendSweetDropCommandFromHeadset()
    {
        if (Object == null || !Object.IsValid)
        {
            Debug.LogError(
                "[WindowsToHeadsetArduino] Cannot send Sweet drop command because NetworkObject is not spawned."
            );

            return;
        }


        if (!Object.HasStateAuthority)
        {
            Debug.LogWarning(
                "[WindowsToHeadsetArduino] Sweet drop command must be sent by State Authority."
            );

            return;
        }


        Debug.Log(
            "[WindowsToHeadsetArduino] Sending Sweet drop command to Windows."
        );


        RPC_SendSweetDropCommandToWindows();
    }


    [Rpc(
        RpcSources.StateAuthority,
        RpcTargets.All,
        Channel = RpcChannel.Reliable)]
    private void RPC_SendSweetDropCommandToWindows()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

        Debug.Log(
            "[WindowsToHeadsetArduino] Windows received Sweet drop command from headset."
        );


        SendToArduino(sweetDropMessage);

#endif
    }


#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

    private void OpenSerialPort()
    {
        if (serialPort != null && serialPort.IsOpen)
            return;


        Debug.Log(
            "[WindowsToHeadsetArduino] Attempting Arduino connection on " +
            portName
        );


        try
        {
            serialPort = new SerialPort(portName, baudRate);

            serialPort.ReadTimeout = 50;

            serialPort.Open();


            Debug.Log(
                "[WindowsToHeadsetArduino] Arduino connected on " +
                portName
            );
        }
        catch (Exception e)
        {
            Debug.LogError(
                "[WindowsToHeadsetArduino] Could not connect to Arduino: " +
                e.Message
            );
        }
    }


    private void SendToArduino(string message)
    {
        if (serialPort == null || !serialPort.IsOpen)
        {
            Debug.LogWarning(
                "[WindowsToHeadsetArduino] Arduino is not connected. " +
                "Could not send: " +
                message
            );

            return;
        }


        try
        {
            serialPort.WriteLine(message);

            Debug.Log(
                "[WindowsToHeadsetArduino] Sent to Arduino: " +
                message
            );
        }
        catch (Exception e)
        {
            Debug.LogError(
                "[WindowsToHeadsetArduino] Arduino write error: " +
                e.Message
            );
        }
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

                Debug.Log(
                    "[WindowsToHeadsetArduino] Arduino serial port closed."
                );
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning(
                "[WindowsToHeadsetArduino] Error closing Arduino: " +
                e.Message
            );
        }
    }

#endif


    private void FindHeadsetNetworkSync()
    {
        if (headsetNetworkSync != null)
            return;


        headsetNetworkSync =
            FindFirstObjectByType<ArduinoDropDiscNetworkSync>();
    }


    public override void Despawned(
        NetworkRunner runner,
        bool hasState)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        CloseSerialPort();
#endif
    }


    private void OnApplicationQuit()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        CloseSerialPort();
#endif
    }


    private void OnDestroy()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        CloseSerialPort();
#endif
    }
}