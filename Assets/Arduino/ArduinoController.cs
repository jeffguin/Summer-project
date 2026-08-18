using UnityEngine;
using System;
using System.Collections;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.IO.Ports;
#endif

public class ArduinoController : MonoBehaviour
{
    [Header("Arduino Serial Settings")]
    public string portName = "COM4";
    public int baudRate = 9600;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private SerialPort serialPort;
#endif


    [Header("TicTacToe Object Spawning")]
    [SerializeField] private GameObject itemIntoVirtualPrefab;
    [SerializeField] private Transform spawnPoint;

    private GameObject spawnedItem;

    private ArduinoDropDiscNetworkSync networkSync;


    [Header("Sweet/Medal Detection, Destroy and Play Animation")]
    [SerializeField] private Collider TriggerCollider;
    [SerializeField] private Animator discAnimator;


    [Header("Arduino Messages")]
    [SerializeField] private string buttonPressMessage = "BUTTON_PRESS";
    [SerializeField] private string rotateCompleteMessage = "ROTATE_COMPLETE";
    [SerializeField] private string sweetCollectedMessage = "2";

    [Header("Animation")]
    [SerializeField] private string fallTriggerName = "Fall";


    [Header("Rotation")]
    public bool canRotate = false;

    [Header("Into Virtual Cooldown")]
    [SerializeField] private float intoVirtualCooldown = 5f;
    [SerializeField] private bool canIntoVirtual = true;


    private void Awake()
    {
        networkSync = GetComponentInParent<ArduinoDropDiscNetworkSync>();
    }


    private void Start()
    {
        // A networked dispenser is initialized by ArduinoDropDiscNetworkSync.Spawned().
        // In the Quest/Windows setup the Windows proxy owns the serial transport,
        // while the Quest host remains authoritative over network gameplay.
        if (networkSync != null)
            return;

        OpenSerialPort();
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
#else
        Debug.Log(
            "Arduino serial transport is disabled on this platform. " +
            "The Windows network peer is expected to own the COM port."
        );
#endif
    }


    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            SendToArduino("2");
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            ArduinoButtonPressed();
        }

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
                    Debug.Log("Arduino button press detected.");
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
#endif
    }


    public void ArduinoButtonPressed()
    {
        if (!canIntoVirtual)
        {
            Debug.Log("Sweet drop is on cooldown.");
            return;
        }

        canIntoVirtual = false;

        Debug.Log("Arduino button pressed.");

        StartCoroutine(IntoVirtualCooldown());

        if (networkSync != null && networkSync.IsNetworkSpawned)
        {
            Debug.Log("Requesting NETWORK Sweet spawn.");

            networkSync.RequestSpawnItemFromHardwarePeer();
            return;
        }

        Debug.Log("Spawning LOCAL Sweet.");

        SpawnItem();
    }


    private IEnumerator IntoVirtualCooldown()
    {
        yield return new WaitForSeconds(intoVirtualCooldown);
        canIntoVirtual = true;
        Debug.Log("Into Virtual cooldown finished.");
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

        if (networkSync != null && networkSync.IsNetworkSpawned)
        {
            networkSync.TryCollectSweetFromStateAuthority(other);
            return;
        }

        HandleSweetCollectedLocally(other);
    }


    private void HandleSweetCollectedLocally(Collider other)
    {

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


    internal void ConfigureNetworkPeer(bool isNetworkSpawned)
    {
        if (isNetworkSpawned)
        {
            OpenSerialPort();
        }
        else
        {
            CloseSerialPort();
        }
    }


    internal void HandleSweetCollectedOnStateAuthority(GameObject sweetObject)
    {
        Debug.Log("Sweet object entered the network-authoritative trigger.");
        ClearSpawnedItemIfMatches(sweetObject);
    }


    internal void SendSweetCollectedToHardwareFromNetwork()
    {
        SendToArduino(sweetCollectedMessage);
    }


    internal void PlayFallingAnimationFromNetwork()
    {
        PlayFallingAnimation();
    }


    internal void PlaySweetDropAnimationFromNetwork()
    {
        PlayFallingAnimation();
    }


    internal bool TryGetItemSpawnData(
        out GameObject prefab,
        out Vector3 spawnPosition,
        out Quaternion spawnRotation)
    {
        prefab = itemIntoVirtualPrefab;
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;

        if (prefab == null)
        {
            Debug.LogError("Item Into Virtual Prefab has not been assigned.");
            return false;
        }

        if (spawnedItem != null)
        {
            Debug.LogWarning("An Item_Into_Virtual is already spawned.");
            return false;
        }

        if (spawnPoint != null)
        {
            spawnPosition = spawnPoint.position;
            spawnRotation = spawnPoint.rotation;
        }

        return true;
    }


    internal void RegisterNetworkSpawnedItem(GameObject item)
    {
        spawnedItem = item;
        Debug.Log("Item_Into_Virtual network-spawned.");
    }


    private void ClearSpawnedItemIfMatches(GameObject sweetObject)
    {
        if (spawnedItem == null || sweetObject == null)
            return;

        if (sweetObject == spawnedItem ||
            sweetObject.transform.IsChildOf(spawnedItem.transform))
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
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
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
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
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
#endif
    }
}