using System;
using System.Collections.Generic;
using Meta.XR.Movement.Networking;
#if FUSION2
using Meta.XR.Movement.Networking.Fusion;
#endif
using Unity.Collections;
using UnityEngine;
using static Meta.XR.Movement.MSDKUtility;
using static Unity.Collections.Allocator;
using static Unity.Collections.NativeArrayOptions;

[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class ActorMovementNetworkHandler : MonoBehaviour, INetworkCharacterHandler
{
    private const int MaximumPacketBytes = 1024;

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
    public Action<int> BytesSent;

    [Header("Movement Retargeting")]
    [SerializeField]
    private NetworkCharacterRetargeter _networkCharacterRetargeter;

    [SerializeField]
    private bool _applyData = true;

    [SerializeField]
    private float _spawnDelay = 0.5f;

    [Header("Network Load")]
    [Tooltip("Hard upper limit for movement packets per second. The local pose " +
             "is still sampled every rendered frame; only network transmission is limited.")]
    [SerializeField]
    [Min(1f)]
    private float _maximumSendRateHz = 12f;

    [Tooltip("Number of preallocated 1024-byte receive slots. When full, the " +
             "oldest packet is discarded so latency and native memory stay bounded.")]
    [SerializeField]
    [Min(2)]
    private int _receiveBufferSize = 3;

    [Header("Diagnostics")]
    [Tooltip("Logs aggregate movement bandwidth and queue pressure at a low rate.")]
    [SerializeField]
    private bool _logNetworkStatistics;

    [SerializeField]
    [Min(1f)]
    private float _networkStatisticsInterval = 5f;

    private INetworkCharacterBehaviour _characterBehaviour;
    private GameObject _character;

    private readonly Dictionary<ulong, int> _clientsLastAck = new();
    private NativeArray<byte>[] _receiveSlots;
    private int[] _receiveLengths;
    private int _receiveReadIndex;
    private int _receiveWriteIndex;
    private int _receiveCount;

    private NativeArray<NativeTransform> _bodyPose;
    private NativeArray<float> _facePose;
    private NativeArray<byte> _serializedData;

    private float _elapsedSendTime;
    private float _elapsedSyncTime;
    private int _dataReadCount;
    private int _configuredFaceShapeCount;

    private float _networkStatisticsElapsed;
    private int _receivedBytesInWindow;
    private int _receivedPacketsInWindow;
    private int _sentBytesInWindow;
    private int _sentPacketsInWindow;
    private int _droppedPacketsInWindow;
    private int _largestReceivedPacketInWindow;
    private int _largestSentPacketInWindow;

    private bool _dataIsValid;
    private bool _setupRequested;
    private bool _instantiateCharacter = true;
    private bool _setupComplete;
    private int _setupCharacterId;

    private bool _loggedSetupFailure;
    private bool _loggedReceiveBeforeReady;
    private bool _loggedDeserializeFailure;
    private bool _loggedOversizedPacket;

    private float EffectiveSendInterval => Mathf.Max(
        _networkCharacterRetargeter != null
            ? _networkCharacterRetargeter.IntervalToSendData
            : 0f,
        1f / Mathf.Max(1f, _maximumSendRateHz)
    );

    private bool ShouldSyncData =>
        _networkCharacterRetargeter != null &&
        _networkCharacterRetargeter.UseSyncInterval &&
        _elapsedSyncTime >= _networkCharacterRetargeter.IntervalToSyncData;

    private bool ShouldSendData =>
        _networkCharacterRetargeter != null &&
        _elapsedSendTime >= EffectiveSendInterval;

    private void Awake()
    {
        _maximumSendRateHz = Mathf.Max(1f, _maximumSendRateHz);
        _receiveBufferSize = Mathf.Max(2, _receiveBufferSize);
        _networkStatisticsInterval = Mathf.Max(
            1f,
            _networkStatisticsInterval
        );

        CreateReceiveBuffers();
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
        UpdateNetworkStatistics();

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
        _maximumSendRateHz = Mathf.Max(1f, _maximumSendRateHz);
        _receiveBufferSize = Mathf.Max(2, _receiveBufferSize);
        _networkStatisticsInterval = Mathf.Max(
            1f,
            _networkStatisticsInterval
        );

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

#if FUSION2
        // Fusion replicates this NetworkArray to every observer. Its
        // ReceiveStreamData implementation ignores clientId, so serializing
        // once per client only repeats native work and overwrites the same
        // state. Use one baseline shared by all clients, or a full snapshot
        // when their acknowledgements differ.
        if (_characterBehaviour is NetworkCharacterBehaviourFusion)
        {
            SendFusionBroadcast(clientIds, localClientId, networkTime);
            ResetSendTimers();
            return;
        }
#endif

        foreach (ulong clientId in clientIds)
        {
            if (clientId == localClientId)
            {
                continue;
            }

            int lastAck = _clientsLastAck.TryGetValue(clientId, out int ack)
                ? ack
                : -1;

            SerializeAndSend(clientId, lastAck, networkTime);
        }

        ResetSendTimers();
    }

#if FUSION2
    private void SendFusionBroadcast(
        ulong[] clientIds,
        ulong localClientId,
        float networkTime)
    {
        bool foundRemoteClient = false;
        bool acknowledgementsMatch = true;
        ulong firstRemoteClientId = 0;
        int sharedAck = -1;

        foreach (ulong clientId in clientIds)
        {
            if (clientId == localClientId)
            {
                continue;
            }

            int clientAck = _clientsLastAck.TryGetValue(clientId, out int ack)
                ? ack
                : -1;

            if (!foundRemoteClient)
            {
                foundRemoteClient = true;
                firstRemoteClientId = clientId;
                sharedAck = clientAck;
            }
            else if (clientAck != sharedAck)
            {
                acknowledgementsMatch = false;
            }
        }

        if (!foundRemoteClient)
        {
            return;
        }

        SerializeAndSend(
            firstRemoteClientId,
            acknowledgementsMatch ? sharedAck : -1,
            networkTime
        );
    }
#endif

    private void SerializeAndSend(
        ulong clientId,
        int lastAck,
        float networkTime)
    {
        try
        {
            SerializeData(lastAck, networkTime);

            if (!_serializedData.IsCreated || _serializedData.Length == 0)
            {
                return;
            }

            if (_serializedData.Length > MaximumPacketBytes)
            {
                if (!_loggedOversizedPacket)
                {
                    Debug.LogError(
                        "ActorMovementNetworkHandler: Serialized movement " +
                        $"packet is {_serializedData.Length} bytes, exceeding " +
                        $"Fusion's {MaximumPacketBytes}-byte capacity. The " +
                        "packet was dropped instead of truncating body or face data.",
                        this
                    );

                    _loggedOversizedPacket = true;
                }

                RecordDroppedPacket();
                return;
            }

            _characterBehaviour.ReceiveStreamData(
                clientId,
                false,
                _serializedData
            );

            RecordSentPacket(_serializedData.Length);
            BytesSent?.Invoke(_serializedData.Length);
        }
        finally
        {
            DisposeSerializedData();
        }
    }

    public void ReceiveData(NativeArray<byte> data)
    {
        if (!data.IsCreated || data.Length == 0)
        {
            return;
        }

        RecordReceivedPacket(data.Length);
        BytesReceived?.Invoke(data.Length);

        if (data.Length > MaximumPacketBytes)
        {
            if (!_loggedOversizedPacket)
            {
                Debug.LogError(
                    "ActorMovementNetworkHandler: Received movement packet " +
                    $"is {data.Length} bytes, exceeding the preallocated " +
                    $"{MaximumPacketBytes}-byte slot. The packet was dropped " +
                    "instead of being truncated.",
                    this
                );

                _loggedOversizedPacket = true;
            }

            RecordDroppedPacket();
            return;
        }

        if (_receiveSlots == null || _receiveSlots.Length == 0)
        {
            CreateReceiveBuffers();
        }

        if (_receiveCount == _receiveSlots.Length)
        {
            _receiveLengths[_receiveReadIndex] = 0;
            _receiveReadIndex =
                (_receiveReadIndex + 1) % _receiveSlots.Length;
            _receiveCount--;
            RecordDroppedPacket();
        }

        NativeArray<byte>.Copy(
            data,
            0,
            _receiveSlots[_receiveWriteIndex],
            0,
            data.Length
        );

        _receiveLengths[_receiveWriteIndex] = data.Length;
        _receiveWriteIndex =
            (_receiveWriteIndex + 1) % _receiveSlots.Length;
        _receiveCount++;
        _dataReadCount++;

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

        if (!ContainsEverySequentialIndex(
                _networkCharacterRetargeter.BodyIndicesToSync,
                jointCount))
        {
            _networkCharacterRetargeter.BodyIndicesToSync =
                CreateSequentialIndices(jointCount);
        }

        if (!ContainsEverySequentialIndex(
                _networkCharacterRetargeter.BodyIndicesToSend,
                jointCount))
        {
            _networkCharacterRetargeter.BodyIndicesToSend =
                CreateSequentialIndices(jointCount);
        }

        if (!ContainsEverySequentialIndex(
                _networkCharacterRetargeter.FaceIndicesToSync,
                _configuredFaceShapeCount))
        {
            _networkCharacterRetargeter.FaceIndicesToSync =
                CreateSequentialIndices(_configuredFaceShapeCount);
        }
    }

    private static bool ContainsEverySequentialIndex(
        int[] indices,
        int requiredCount)
    {
        if (indices == null || indices.Length != requiredCount)
        {
            return false;
        }

        for (int i = 0; i < requiredCount; i++)
        {
            if (indices[i] != i)
            {
                return false;
            }
        }

        return true;
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

        if (_receiveCount > 0)
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
        int slotIndex = _receiveReadIndex;
        int dataLength = _receiveLengths[slotIndex];

        _receiveLengths[slotIndex] = 0;
        _receiveReadIndex =
            (_receiveReadIndex + 1) % _receiveSlots.Length;
        _receiveCount--;

        if (dataLength <= 0)
        {
            return;
        }

        // GetSubArray creates a non-owning view. The persistent owner remains
        // allocated in the ring and is reused by the next received packet.
        NativeArray<byte> data =
            _receiveSlots[slotIndex].GetSubArray(0, dataLength);

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
        // This method runs from LateUpdate, so use actual frame time. Fusion's
        // Runner.DeltaTime is a simulation-tick duration and can overcount when
        // the headset renders at a different rate (for example 72 or 90 Hz).
        float deltaTime = Mathf.Max(0f, Time.unscaledDeltaTime);
        _elapsedSendTime = Mathf.Min(
            _elapsedSendTime + deltaTime,
            EffectiveSendInterval
        );

        if (_networkCharacterRetargeter.UseSyncInterval)
        {
            float syncInterval = Mathf.Max(
                0.0001f,
                _networkCharacterRetargeter.IntervalToSyncData
            );

            _elapsedSyncTime = Mathf.Min(
                _elapsedSyncTime + deltaTime,
                syncInterval
            );
        }

        if (ShouldSendData)
        {
            SendData(networkTime);
        }
    }

    private void SerializeData(int lastAck, float networkTime)
    {
        DisposeSerializedData();

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
            _elapsedSyncTime = 0f;
        }

        // Do not subtract the interval. After a hitch that would leave a large
        // remainder and cause catch-up packets on consecutive render frames.
        _elapsedSendTime = 0f;
    }

    private void ToggleCharacterWhenReady()
    {
        if (!_setupComplete || _networkCharacterRetargeter == null)
        {
            return;
        }

        float interval = EffectiveSendInterval;

        if (_dataReadCount >= _spawnDelay / interval)
        {
            _networkCharacterRetargeter.ToggleObjects(true);
        }
    }

    private void RecordReceivedPacket(int byteCount)
    {
        if (!_logNetworkStatistics)
        {
            return;
        }

        _receivedBytesInWindow += byteCount;
        _receivedPacketsInWindow++;
        _largestReceivedPacketInWindow = Mathf.Max(
            _largestReceivedPacketInWindow,
            byteCount
        );
    }

    private void RecordSentPacket(int byteCount)
    {
        if (!_logNetworkStatistics)
        {
            return;
        }

        _sentBytesInWindow += byteCount;
        _sentPacketsInWindow++;
        _largestSentPacketInWindow = Mathf.Max(
            _largestSentPacketInWindow,
            byteCount
        );
    }

    private void RecordDroppedPacket()
    {
        if (_logNetworkStatistics)
        {
            _droppedPacketsInWindow++;
        }
    }

    private void UpdateNetworkStatistics()
    {
        if (!_logNetworkStatistics)
        {
            return;
        }

        _networkStatisticsElapsed += Time.unscaledDeltaTime;

        if (_networkStatisticsElapsed < _networkStatisticsInterval)
        {
            return;
        }

        float elapsed = Mathf.Max(0.0001f, _networkStatisticsElapsed);
        int packetCount = _sentPacketsInWindow +
                          _receivedPacketsInWindow +
                          _droppedPacketsInWindow;

        if (packetCount > 0)
        {
            float sentKilobitsPerSecond =
                _sentBytesInWindow * 8f / elapsed / 1000f;
            float receivedKilobitsPerSecond =
                _receivedBytesInWindow * 8f / elapsed / 1000f;

            Debug.Log(
                "ActorMovementNetworkHandler: movement network stats - " +
                $"TX {sentKilobitsPerSecond:F1} kbit/s " +
                $"({_sentPacketsInWindow} packets, max " +
                $"{_largestSentPacketInWindow} B), " +
                $"RX {receivedKilobitsPerSecond:F1} kbit/s " +
                $"({_receivedPacketsInWindow} packets, max " +
                $"{_largestReceivedPacketInWindow} B), " +
                $"queue {_receiveCount}/{_receiveBufferSize}, " +
                $"dropped {_droppedPacketsInWindow}.",
                this
            );
        }

        _networkStatisticsElapsed = 0f;
        _receivedBytesInWindow = 0;
        _receivedPacketsInWindow = 0;
        _sentBytesInWindow = 0;
        _sentPacketsInWindow = 0;
        _droppedPacketsInWindow = 0;
        _largestReceivedPacketInWindow = 0;
        _largestSentPacketInWindow = 0;
    }

    private void ResetForCharacterChange()
    {
        _setupComplete = false;
        _setupCharacterId = 0;
        _dataIsValid = false;
        _dataReadCount = 0;
        _configuredFaceShapeCount = 0;
        _networkCharacterRetargeter = null;
        _loggedOversizedPacket = false;

        DisposePoseBuffers();
        ClearReceiveQueue();

        if (_character != null && _character != gameObject)
        {
            Destroy(_character);
        }

        _character = null;
    }

    private void DisposeNativeData()
    {
        DisposeSerializedData();
        DisposeReceiveBuffers();
        DisposePoseBuffers();
    }

    private void CreateReceiveBuffers()
    {
        DisposeReceiveBuffers();

        int slotCount = Mathf.Max(2, _receiveBufferSize);
        _receiveSlots = new NativeArray<byte>[slotCount];
        _receiveLengths = new int[slotCount];

        for (int i = 0; i < slotCount; i++)
        {
            _receiveSlots[i] = new NativeArray<byte>(
                MaximumPacketBytes,
                Persistent,
                UninitializedMemory
            );
        }

        ClearReceiveQueue();
    }

    private void ClearReceiveQueue()
    {
        if (_receiveLengths != null)
        {
            Array.Clear(_receiveLengths, 0, _receiveLengths.Length);
        }

        _receiveReadIndex = 0;
        _receiveWriteIndex = 0;
        _receiveCount = 0;
    }

    private void DisposeReceiveBuffers()
    {
        if (_receiveSlots != null)
        {
            for (int i = 0; i < _receiveSlots.Length; i++)
            {
                if (_receiveSlots[i].IsCreated)
                {
                    _receiveSlots[i].Dispose();
                }
            }
        }

        _receiveSlots = null;
        _receiveLengths = null;
        _receiveReadIndex = 0;
        _receiveWriteIndex = 0;
        _receiveCount = 0;
    }

    private void DisposeSerializedData()
    {
        if (_serializedData.IsCreated)
        {
            _serializedData.Dispose();
            _serializedData = default;
        }
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
