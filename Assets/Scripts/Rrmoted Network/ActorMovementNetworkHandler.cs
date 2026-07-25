using System;
using System.Collections.Generic;
using Meta.XR.Movement.Networking;
using Unity.Collections;
using UnityEngine;
using static Meta.XR.Movement.MSDKUtility;
using static Unity.Collections.Allocator;
using static Unity.Collections.NativeArrayOptions;

[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class ActorMovementNetworkHandler : MonoBehaviour, INetworkCharacterHandler
{
    public INetworkCharacterBehaviour CharacterBehaviour => _characterBehaviour;
    public GameObject Character => _character;
    public NetworkCharacterRetargeter NetworkCharacterRetargeter => _networkCharacterRetargeter;
    public bool IsSetupComplete => _setupComplete;

    public bool ApplyData
    {
        get => _applyData;
        set => _applyData = value;
    }

    public Action<int> BytesReceived;

    [Header("Movement Retargeting")]
    [SerializeField]
    private NetworkCharacterRetargeter _networkCharacterRetargeter;

    [SerializeField]
    private bool _applyData = true;

    [SerializeField]
    private float _spawnDelay = 0.5f;

    [Header("Safety")]
    [Tooltip("Maximum packets retained while the remote character is still being initialized.")]
    [SerializeField]
    [Min(1)]
    private int _fallbackBufferSize = 5;

    private INetworkCharacterBehaviour _characterBehaviour;
    private GameObject _character;

    private readonly Dictionary<ulong, int> _clientsLastAck = new();
    private Queue<NativeArray<byte>> _streamedData;

    private NativeArray<NativeTransform> _bodyPose;
    private NativeArray<float> _facePose;
    private NativeArray<byte> _serializedData;

    private float _elapsedSendTime;
    private float _elapsedSyncTime;
    private int _dataReadCount;
    private int _configuredFaceShapeCount;

    private bool _dataIsValid;
    private bool _setupRequested;
    private bool _instantiateCharacter = true;
    private bool _setupComplete;
    private int _setupCharacterId;

    private bool _loggedSetupFailure;
    private bool _loggedReceiveBeforeReady;
    private bool _loggedDeserializeFailure;

    private bool ShouldSyncData =>
        _networkCharacterRetargeter != null &&
        _networkCharacterRetargeter.UseSyncInterval &&
        _elapsedSyncTime >= _networkCharacterRetargeter.IntervalToSyncData;

    private bool ShouldSendData =>
        _networkCharacterRetargeter != null &&
        (ShouldSyncData ||
         _elapsedSendTime >= _networkCharacterRetargeter.IntervalToSendData);

    private void Awake()
    {
        _characterBehaviour = GetComponent<INetworkCharacterBehaviour>();

        if (_networkCharacterRetargeter == null)
        {
            _networkCharacterRetargeter =
                GetComponentInChildren<NetworkCharacterRetargeter>(true);
        }
    }

    private void Start()
    {
        if (_characterBehaviour == null)
        {
            Debug.LogError(
                "ActorMovementNetworkHandler: An INetworkCharacterBehaviour " +
                "component is required on the same GameObject.",
                this
            );

            enabled = false;
        }
    }

    private void Update()
    {
        if (_setupRequested && !_setupComplete)
        {
            TryCompleteSetup();
        }

        if (!_setupComplete ||
            _characterBehaviour == null ||
            _characterBehaviour.HasInputAuthority)
        {
            return;
        }

        TryReceiveData(
            _characterBehaviour.NetworkTime,
            _characterBehaviour.RenderTime
        );
    }

    private void LateUpdate()
    {
        if (!_setupComplete ||
            _characterBehaviour == null ||
            !_characterBehaviour.HasInputAuthority)
        {
            return;
        }

        TrySendData(_characterBehaviour.NetworkTime);
    }

    private void OnDestroy()
    {
        _clientsLastAck.Clear();
        DisposeNativeData();
    }

    private void OnValidate()
    {
        _fallbackBufferSize = Mathf.Max(1, _fallbackBufferSize);

        if (_networkCharacterRetargeter != null &&
            _networkCharacterRetargeter.Owner ==
            NetworkCharacterRetargeter.Ownership.Host)
        {
            _networkCharacterRetargeter.UpdateSerializationSettings();
        }
    }

    public void Setup(bool instantiateCharacter = true)
    {
        if (_characterBehaviour == null)
        {
            _characterBehaviour = GetComponent<INetworkCharacterBehaviour>();
        }

        if (_characterBehaviour == null)
        {
            LogSetupFailure(
                "INetworkCharacterBehaviour was not found on the network root."
            );
            return;
        }

        int characterId = _characterBehaviour.CharacterId;

        if (_setupComplete &&
            _setupCharacterId == characterId &&
            _character != null)
        {
            return;
        }

        if (_setupComplete && _setupCharacterId != characterId)
        {
            ResetForCharacterChange();
        }

        _setupRequested = true;
        _instantiateCharacter = instantiateCharacter;
        TryCompleteSetup();
    }

    public void SendData(float networkTime)
    {
        if (!_setupComplete ||
            _networkCharacterRetargeter == null ||
            !_networkCharacterRetargeter.IsValid ||
            !_networkCharacterRetargeter.SkeletonRetargeter.IsInitialized ||
            !_networkCharacterRetargeter.SkeletonRetargeter.AppliedPose)
        {
            return;
        }

        ulong localClientId = _characterBehaviour.LocalClientId;
        ulong[] clientIds = _characterBehaviour.ClientIds;

        if (clientIds == null)
        {
            return;
        }

        foreach (ulong clientId in clientIds)
        {
            if (clientId == localClientId)
            {
                continue;
            }

            int lastAck = _clientsLastAck.TryGetValue(clientId, out int ack)
                ? ack
                : -1;

            SerializeData(lastAck, networkTime);

            if (_serializedData.IsCreated && _serializedData.Length > 0)
            {
                _characterBehaviour.ReceiveStreamData(
                    clientId,
                    false,
                    _serializedData
                );
            }
        }

        ResetSendTimers();
    }

    public void ReceiveData(NativeArray<byte> data)
    {
        if (!data.IsCreated || data.Length == 0)
        {
            return;
        }

        int maxBufferSize = GetMaxBufferSize();
        _streamedData ??= new Queue<NativeArray<byte>>(maxBufferSize);

        while (_streamedData.Count >= maxBufferSize)
        {
            NativeArray<byte> discarded = _streamedData.Dequeue();

            if (discarded.IsCreated)
            {
                discarded.Dispose();
            }
        }

        var copy = new NativeArray<byte>(
            data.Length,
            Persistent,
            UninitializedMemory
        );

        data.CopyTo(copy);
        _streamedData.Enqueue(copy);
        _dataReadCount++;

        BytesReceived?.Invoke(data.Length);

        if (!_setupComplete)
        {
            if (!_loggedReceiveBeforeReady)
            {
                Debug.LogWarning(
                    "ActorMovementNetworkHandler: Movement data arrived before " +
                    "the remote character finished Setup. The packet is buffered.",
                    this
                );

                _loggedReceiveBeforeReady = true;
            }

            return;
        }

        ToggleCharacterWhenReady();
    }

    public void SendAck(int ack)
    {
        if (_characterBehaviour == null)
        {
            return;
        }

        _characterBehaviour.ReceiveStreamAck(
            _characterBehaviour.LocalClientId,
            ack
        );
    }

    public void ReceiveAck(ulong id, int ack)
    {
        _clientsLastAck[id] = ack;
    }

    private bool TryCompleteSetup()
    {
        if (_setupComplete)
        {
            return true;
        }

        if (_characterBehaviour == null)
        {
            LogSetupFailure(
                "Setup cannot continue because the network behaviour is missing."
            );
            return false;
        }

        if (_instantiateCharacter)
        {
            if (_characterBehaviour.CharacterId <= 0)
            {
                return false;
            }

            if (_character == null)
            {
                GameObject prefab;

                try
                {
                    prefab = _characterBehaviour.CharacterPrefab;
                }
                catch (Exception exception)
                {
                    LogSetupFailure(
                        "The CharacterId does not resolve to a valid catalog " +
                        $"entry. {exception.Message}"
                    );
                    return false;
                }

                if (prefab == null)
                {
                    LogSetupFailure(
                        "The CharacterId resolves to a null character prefab."
                    );
                    return false;
                }

                _character = Instantiate(prefab, transform, false);
            }
        }
        else
        {
            _character = gameObject;
        }

        if (_networkCharacterRetargeter == null)
        {
            _networkCharacterRetargeter =
                _character.GetComponentInChildren<NetworkCharacterRetargeter>(
                    true
                );
        }

        if (_networkCharacterRetargeter == null)
        {
            LogSetupFailure(
                "NetworkCharacterRetargeter was not found in the character."
            );
            return false;
        }

        ApplyOwnership();

        if (!EnsureRetargetingInitialized())
        {
            return false;
        }

        _networkCharacterRetargeter.UpdateSerializationSettings();

        if (!EnsurePoseBuffers())
        {
            return false;
        }

        EnsureSerializationIndices();

        _setupCharacterId = _characterBehaviour.CharacterId;
        _setupComplete = true;
        _loggedSetupFailure = false;
        _loggedReceiveBeforeReady = false;

        if (_characterBehaviour.HasInputAuthority)
        {
            _networkCharacterRetargeter.ToggleObjects(true);
        }
        else
        {
            ToggleCharacterWhenReady();
        }

        Debug.Log(
            "ActorMovementNetworkHandler: Setup completed. " +
            $"CharacterId={_setupCharacterId}, " +
            $"Owner={_networkCharacterRetargeter.Owner}, " +
            $"Joints={_bodyPose.Length}, " +
            $"FaceShapes={_configuredFaceShapeCount}.",
            this
        );

        return true;
    }

    private void ApplyOwnership()
    {
        NetworkCharacterRetargeter.Ownership expectedOwnership =
            _characterBehaviour.HasInputAuthority
                ? NetworkCharacterRetargeter.Ownership.Host
                : NetworkCharacterRetargeter.Ownership.Client;

        _networkCharacterRetargeter.Owner = expectedOwnership;

        gameObject.name = expectedOwnership ==
                          NetworkCharacterRetargeter.Ownership.Host
            ? "LocalCharacter"
            : "RemoteCharacter";
    }

    private bool EnsureRetargetingInitialized()
    {
        if (_networkCharacterRetargeter.RetargetingHandle != INVALID_HANDLE)
        {
            return true;
        }

        if (_networkCharacterRetargeter.ConfigAsset == null)
        {
            LogSetupFailure(
                "The character retargeter ConfigAsset is null."
            );
            return false;
        }

        if (string.IsNullOrEmpty(_networkCharacterRetargeter.Config))
        {
            LogSetupFailure(
                "The character retargeter configuration is empty."
            );
            return false;
        }

        try
        {
            _networkCharacterRetargeter.Setup(
                _networkCharacterRetargeter.Config
            );
        }
        catch (Exception exception)
        {
            LogSetupFailure(
                $"Retargeter Setup threw an exception: {exception}"
            );
            return false;
        }

        if (_networkCharacterRetargeter.RetargetingHandle == INVALID_HANDLE)
        {
            LogSetupFailure(
                "Retargeter Setup completed without creating a valid handle."
            );
            return false;
        }

        return true;
    }

    private bool EnsurePoseBuffers()
    {
        if (_networkCharacterRetargeter == null)
        {
            return false;
        }

        int jointCount = _networkCharacterRetargeter.NumberOfJoints;
        int faceShapeCount = _networkCharacterRetargeter.NumberOfShapes;

        if (jointCount <= 0)
        {
            LogSetupFailure(
                $"The character has an invalid joint count ({jointCount})."
            );
            return false;
        }

        EnsureArraySize(ref _bodyPose, jointCount);

        _configuredFaceShapeCount = Mathf.Max(0, faceShapeCount);

        // SDK v83 dereferences the face pointer unconditionally. Keep a
        // one-element buffer for body-only characters so the pointer is valid.
        int faceBufferLength = Mathf.Max(1, _configuredFaceShapeCount);
        EnsureArraySize(ref _facePose, faceBufferLength);

        bool ready = _bodyPose.IsCreated &&
                     _bodyPose.Length == jointCount &&
                     _facePose.IsCreated &&
                     _facePose.Length == faceBufferLength;

        if (!ready)
        {
            LogSetupFailure(
                "Body or face pose buffers could not be created."
            );
        }

        return ready;
    }

    private static void EnsureArraySize<T>(
        ref NativeArray<T> array,
        int requiredLength)
        where T : struct
    {
        if (array.IsCreated && array.Length == requiredLength)
        {
            return;
        }

        if (array.IsCreated)
        {
            array.Dispose();
        }

        array = new NativeArray<T>(
            requiredLength,
            Persistent,
            ClearMemory
        );
    }

    private void EnsureSerializationIndices()
    {
        int jointCount = _networkCharacterRetargeter.NumberOfJoints;

        if (_networkCharacterRetargeter.BodyIndicesToSync == null ||
            _networkCharacterRetargeter.BodyIndicesToSync.Length == 0)
        {
            _networkCharacterRetargeter.BodyIndicesToSync =
                CreateSequentialIndices(jointCount);
        }

        if (_networkCharacterRetargeter.BodyIndicesToSend == null ||
            _networkCharacterRetargeter.BodyIndicesToSend.Length == 0)
        {
            _networkCharacterRetargeter.BodyIndicesToSend =
                CreateSequentialIndices(jointCount);
        }

        if ((_networkCharacterRetargeter.FaceIndicesToSync == null ||
             _networkCharacterRetargeter.FaceIndicesToSync.Length == 0) &&
            _configuredFaceShapeCount > 0)
        {
            _networkCharacterRetargeter.FaceIndicesToSync =
                CreateSequentialIndices(_configuredFaceShapeCount);
        }
    }

    private static int[] CreateSequentialIndices(int count)
    {
        var indices = new int[Mathf.Max(0, count)];

        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }

        return indices;
    }

    private void TryReceiveData(float networkTime, float renderTime)
    {
        if (!_setupComplete ||
            _networkCharacterRetargeter == null ||
            _networkCharacterRetargeter.RetargetingHandle == INVALID_HANDLE ||
            !EnsurePoseBuffers())
        {
            return;
        }

        if (_streamedData is { Count: > 0 })
        {
            DeserializeNextPacket();
        }

        if (!_applyData || !_dataIsValid)
        {
            return;
        }

        if (ReadBodyData(renderTime))
        {
            _networkCharacterRetargeter.ApplyBodyPose(
                _bodyPose,
                Meta.XR.Movement.Retargeting.JointType.NoWorldSpace
            );

            _networkCharacterRetargeter.SetDebugPose(_bodyPose);
        }

        if (_configuredFaceShapeCount > 0 && ReadFaceData(renderTime))
        {
            _networkCharacterRetargeter.ApplyFacePose(_facePose);
        }
    }

    private void DeserializeNextPacket()
    {
        NativeArray<byte> data = _streamedData.Dequeue();

        try
        {
            bool success = DeserializeSkeletonAndFace(
                _networkCharacterRetargeter.RetargetingHandle,
                data,
                SERIALIZATION_VERSION_CURRENT,
                out _,
                out _,
                out int ack,
                ref _bodyPose,
                ref _facePose
            );

            if (!success)
            {
                _dataIsValid = false;
                return;
            }

            if (_configuredFaceShapeCount > 0)
            {
                _networkCharacterRetargeter.DeNormalizeFaceValues(
                    ref _facePose
                );
            }

            _dataIsValid = true;
            _loggedDeserializeFailure = false;
            SendAck(ack);
        }
        catch (Exception exception)
        {
            _dataIsValid = false;

            if (!_loggedDeserializeFailure)
            {
                Debug.LogError(
                    "ActorMovementNetworkHandler: Movement deserialization " +
                    $"failed. {exception}",
                    this
                );

                _loggedDeserializeFailure = true;
            }
        }
        finally
        {
            if (data.IsCreated)
            {
                data.Dispose();
            }
        }
    }

    private bool ReadBodyData(float renderTime)
    {
        if (!_dataIsValid)
        {
            return false;
        }

        if (!_networkCharacterRetargeter.UseInterpolation)
        {
            return true;
        }

        return GetInterpolatedSkeleton(
            _networkCharacterRetargeter.RetargetingHandle,
            SkeletonType.TargetSkeleton,
            ref _bodyPose,
            renderTime
        );
    }

    private bool ReadFaceData(float renderTime)
    {
        if (!_dataIsValid || _configuredFaceShapeCount <= 0)
        {
            return false;
        }

        if (!_networkCharacterRetargeter.UseInterpolation)
        {
            return true;
        }

        if (!GetInterpolatedFace(
                _networkCharacterRetargeter.RetargetingHandle,
                SkeletonType.TargetSkeleton,
                ref _facePose,
                renderTime))
        {
            return false;
        }

        _networkCharacterRetargeter.DeNormalizeFaceValues(ref _facePose);
        return true;
    }

    private void TrySendData(float networkTime)
    {
        _elapsedSendTime += _characterBehaviour.DeltaTime;
        _elapsedSyncTime += _characterBehaviour.DeltaTime;

        if (ShouldSendData)
        {
            SendData(networkTime);
        }
    }

    private void SerializeData(int lastAck, float networkTime)
    {
        if (!_setupComplete ||
            _networkCharacterRetargeter.RetargetingHandle == INVALID_HANDLE)
        {
            return;
        }

        if (ShouldSyncData ||
            !_networkCharacterRetargeter.UseDeltaCompression)
        {
            lastAck = -1;
        }

        NativeArray<NativeTransform> bodyPose =
            _networkCharacterRetargeter.GetCurrentBodyPose(
                Meta.XR.Movement.Retargeting.JointType.NoWorldSpace
            );

        NativeArray<float> facePose =
            _networkCharacterRetargeter.GetCurrentFacePose(true);

        int[] bodyIndices = lastAck == -1
            ? _networkCharacterRetargeter.BodyIndicesToSync
            : _networkCharacterRetargeter.BodyIndicesToSend;

        int[] faceIndices =
            _networkCharacterRetargeter.FaceIndicesToSync ??
            Array.Empty<int>();

        try
        {
            _dataIsValid = SerializeSkeletonAndFace(
                _networkCharacterRetargeter.RetargetingHandle,
                networkTime,
                bodyPose,
                facePose,
                lastAck,
                bodyIndices ?? Array.Empty<int>(),
                faceIndices,
                ref _serializedData
            );
        }
        finally
        {
            if (bodyPose.IsCreated)
            {
                bodyPose.Dispose();
            }

            if (facePose.IsCreated)
            {
                facePose.Dispose();
            }
        }
    }

    private void ResetSendTimers()
    {
        if (ShouldSyncData)
        {
            _elapsedSyncTime -=
                _networkCharacterRetargeter.IntervalToSyncData;

            _elapsedSendTime = 0f;
        }
        else if (ShouldSendData)
        {
            _elapsedSendTime -=
                _networkCharacterRetargeter.IntervalToSendData;
        }
    }

    private int GetMaxBufferSize()
    {
        if (_networkCharacterRetargeter != null)
        {
            return Mathf.Max(
                1,
                _networkCharacterRetargeter.MaxBufferSize
            );
        }

        return Mathf.Max(1, _fallbackBufferSize);
    }

    private void ToggleCharacterWhenReady()
    {
        if (!_setupComplete || _networkCharacterRetargeter == null)
        {
            return;
        }

        float interval = Mathf.Max(
            0.0001f,
            _networkCharacterRetargeter.IntervalToSendData
        );

        if (_dataReadCount >= _spawnDelay / interval)
        {
            _networkCharacterRetargeter.ToggleObjects(true);
        }
    }

    private void ResetForCharacterChange()
    {
        _setupComplete = false;
        _setupCharacterId = 0;
        _dataIsValid = false;
        _dataReadCount = 0;
        _configuredFaceShapeCount = 0;
        _networkCharacterRetargeter = null;

        DisposePoseBuffers();
        DisposeStreamedData();

        if (_character != null && _character != gameObject)
        {
            Destroy(_character);
        }

        _character = null;
    }

    private void DisposeNativeData()
    {
        DisposeStreamedData();
        DisposePoseBuffers();
    }

    private void DisposeStreamedData()
    {
        if (_streamedData == null)
        {
            return;
        }

        while (_streamedData.Count > 0)
        {
            NativeArray<byte> data = _streamedData.Dequeue();

            if (data.IsCreated)
            {
                data.Dispose();
            }
        }

        _streamedData = null;
    }

    private void DisposePoseBuffers()
    {
        if (_bodyPose.IsCreated)
        {
            _bodyPose.Dispose();
        }

        if (_facePose.IsCreated)
        {
            _facePose.Dispose();
        }
    }

    private void LogSetupFailure(string message)
    {
        if (_loggedSetupFailure)
        {
            return;
        }

        Debug.LogError(
            $"ActorMovementNetworkHandler: {message}",
            this
        );

        _loggedSetupFailure = true;
    }
}
