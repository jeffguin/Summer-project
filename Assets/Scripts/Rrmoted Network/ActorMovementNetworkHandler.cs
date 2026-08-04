using System;
using System.Collections.Generic;
using Meta.XR.Movement.Networking;
using Meta.XR.Movement.Networking.Local;
using Meta.XR.Movement.Retargeting;
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

    [Header("Isolation Test")]
    [Tooltip("On a remote Fusion instance, keeps the Fusion payload callback " +
             "and receive counters, then discards the payload before persistent " +
             "queueing or Movement SDK deserialization. This setting is ignored " +
             "by the input-authority actor.")]
    [SerializeField]
    private bool _receiveAndDiscardRemotePayloads;

    [Tooltip("When receive-and-discard is enabled, also instantiate the remote " +
             "avatar and initialize its NetworkCharacterRetargeter before " +
             "discarding payloads. The avatar remains in its initial pose.")]
    [SerializeField]
    private bool _initializeRemoteAvatarBeforeDiscarding;

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

    [Header("Stream Safety")]
    [Tooltip("Forces remote avatars to apply only newly deserialized poses. " +
             "This bypasses the Movement SDK interpolation buffer while diagnosing stalls.")]
    [SerializeField]
    private bool _disableRemoteInterpolation = true;

    [Tooltip("Stops applying pose data after the movement stream becomes stale.")]
    [SerializeField]
    private bool _enableStalePacketProtection = true;

    [SerializeField]
    [Min(0.1f)]
    private float _stalePacketTimeoutSeconds = 0.5f;

    [Tooltip("Rejects a complete pose when it contains non-finite or unsafe transform values.")]
    [SerializeField]
    private bool _validateReceivedPose = true;

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
    private int _callbackOnlyDiscardedPacketsInWindow;
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
    private bool _loggedInvalidPose;
    private bool _loggedApplyFailure;
    private bool _loggedCallbackOnlySetup;
    private bool _loggedCallbackOnlyPacket;

    private float _lastPacketArrivalRealtime;
    private double _latestSnapshotTimestamp;
    private bool _hasReceivedPacket;
    private bool _packetStreamIsStale;

    private bool _hasObservedLocalTrackingState;
    private bool _lastLocalTrackingState;
    private bool _forceFullSnapshot = true;
    private bool _lastSerializedPacketWasFullSnapshot;

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

    private bool IsRemoteReceiveDiscardMode =>
        _receiveAndDiscardRemotePayloads &&
        _characterBehaviour != null &&
        !_characterBehaviour.HasInputAuthority;

    private bool IsRemoteAvatarReadyDiscardMode =>
        IsRemoteReceiveDiscardMode &&
        _initializeRemoteAvatarBeforeDiscarding;

    private void Awake()
    {
        _maximumSendRateHz = Mathf.Max(1f, _maximumSendRateHz);
        _receiveBufferSize = Mathf.Max(2, _receiveBufferSize);
        _stalePacketTimeoutSeconds = Mathf.Max(
            0.1f,
            _stalePacketTimeoutSeconds
        );
        _networkStatisticsInterval = Mathf.Max(
            1f,
            _networkStatisticsInterval
        );

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
            _characterBehaviour.HasInputAuthority ||
            IsRemoteReceiveDiscardMode)
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

        UpdateLocalTrackingState();
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
        _stalePacketTimeoutSeconds = Mathf.Max(
            0.1f,
            _stalePacketTimeoutSeconds
        );
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

        if (IsRemoteReceiveDiscardMode &&
            !_initializeRemoteAvatarBeforeDiscarding)
        {
            if (!_setupComplete ||
                _setupCharacterId != characterId ||
                !_loggedCallbackOnlySetup)
            {
                ConfigureFusionCallbackOnlyMode(characterId);
            }

            return;
        }

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
            !IsLocalTrackingReady())
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

            if (_lastSerializedPacketWasFullSnapshot)
            {
                _forceFullSnapshot = false;
            }

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

        if (IsRemoteReceiveDiscardMode)
        {
            _dataReadCount++;
            _lastPacketArrivalRealtime = Time.realtimeSinceStartup;
            _hasReceivedPacket = true;
            _callbackOnlyDiscardedPacketsInWindow++;

            if (!_loggedCallbackOnlyPacket)
            {
                string mode = IsRemoteAvatarReadyDiscardMode
                    ? "AVATAR_READY_RECEIVE_DISCARD"
                    : "FUSION_CALLBACK_ONLY";
                bool avatarInstantiated =
                    _character != null && _character != gameObject;
                bool retargeterInitialized =
                    _networkCharacterRetargeter != null &&
                    _networkCharacterRetargeter.RetargetingHandle !=
                    INVALID_HANDLE;

                Debug.Log(
                    $"ActorMovementNetworkHandler: {mode} " +
                    $"received {data.Length} bytes and discarded the payload. " +
                    "PersistentQueue=False, MovementDeserialize=False, " +
                    "AckSent=False, PoseApplied=False, " +
                    $"AvatarInstantiated={avatarInstantiated}, " +
                    $"RetargeterInitialized={retargeterInitialized}.",
                    this
                );

                _loggedCallbackOnlyPacket = true;
            }

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
        _lastPacketArrivalRealtime = Time.realtimeSinceStartup;
        _hasReceivedPacket = true;

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
        ConfigureTrackingComponentsForOwnership();

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

        if (_characterBehaviour.HasInputAuthority ||
            IsRemoteAvatarReadyDiscardMode)
        {
            _networkCharacterRetargeter.ToggleObjects(true);
        }
        else
        {
            ToggleCharacterWhenReady();
        }

        string setupMode = IsRemoteAvatarReadyDiscardMode
            ? "AvatarReadyReceiveDiscard"
            : "Normal";

        if (IsRemoteAvatarReadyDiscardMode)
        {
            gameObject.name = "RemoteCharacterAvatarReadyReceiveDiscard";
        }

        Debug.Log(
            "ActorMovementNetworkHandler: Setup completed. " +
            $"Mode={setupMode}, " +
            $"CharacterId={_setupCharacterId}, " +
            $"Owner={_networkCharacterRetargeter.Owner}, " +
            $"Joints={_bodyPose.Length}, " +
            $"FaceShapes={_configuredFaceShapeCount}, " +
            $"ApplyData={_applyData}, " +
            $"PersistentQueue={!IsRemoteReceiveDiscardMode}, " +
            $"MovementDeserialize={!IsRemoteReceiveDiscardMode}, " +
            $"AckEnabled={!IsRemoteReceiveDiscardMode}, " +
            $"PoseApplied={!IsRemoteReceiveDiscardMode && _applyData}, " +
            $"UseInterpolation={_networkCharacterRetargeter.UseInterpolation}, " +
            $"StaleTimeout={_stalePacketTimeoutSeconds:F2}s, " +
            $"ValidatePose={_validateReceivedPose}.",
            this
        );

        return true;
    }

    private void ConfigureFusionCallbackOnlyMode(int characterId)
    {
        _setupRequested = false;
        _instantiateCharacter = false;
        _setupCharacterId = characterId;
        _setupComplete = true;
        _dataIsValid = false;
        _configuredFaceShapeCount = 0;

        DisposeReceiveBuffers();
        DisposePoseBuffers();

        if (_character != null && _character != gameObject)
        {
            Destroy(_character);
        }

        _character = null;
        _networkCharacterRetargeter = null;
        gameObject.name = "RemoteCharacterFusionCallbackOnly";

        Debug.Log(
            "ActorMovementNetworkHandler: Setup completed. " +
            $"Mode=FusionCallbackOnly, CharacterId={characterId}, " +
            "Owner=Remote, AvatarInstantiated=False, PersistentQueue=False, " +
            "MovementDeserialize=False.",
            this
        );

        _loggedCallbackOnlySetup = true;
    }

    private void ApplyOwnership()
    {
        NetworkCharacterRetargeter.Ownership expectedOwnership =
            _characterBehaviour.HasInputAuthority
                ? NetworkCharacterRetargeter.Ownership.Host
                : NetworkCharacterRetargeter.Ownership.Client;

        _networkCharacterRetargeter.Owner = expectedOwnership;

        if (!_characterBehaviour.HasInputAuthority &&
            _disableRemoteInterpolation)
        {
            _networkCharacterRetargeter.UseInterpolation = false;
        }

        gameObject.name = expectedOwnership ==
                          NetworkCharacterRetargeter.Ownership.Host
            ? "LocalCharacter"
            : "RemoteCharacter";
    }

    private void ConfigureTrackingComponentsForOwnership()
    {
        bool isLocalTrackingSource =
            _characterBehaviour.HasInputAuthority;

        MetaSourceDataProvider[] bodySources =
            _character.GetComponentsInChildren<MetaSourceDataProvider>(true);

        foreach (MetaSourceDataProvider bodySource in bodySources)
        {
            bodySource.enabled = isLocalTrackingSource;
        }

        OVRFaceExpressions[] faceSources =
            _character.GetComponentsInChildren<OVRFaceExpressions>(true);

        foreach (OVRFaceExpressions faceSource in faceSources)
        {
            faceSource.enabled = isLocalTrackingSource;
        }

        Meta.XR.Movement.Networking.NetworkCharacterHandler[]
            sampleHandlers =
                _character.GetComponentsInChildren<
                    Meta.XR.Movement.Networking.NetworkCharacterHandler
                >(true);

        foreach (Meta.XR.Movement.Networking.NetworkCharacterHandler
                 sampleHandler in sampleHandlers)
        {
            sampleHandler.enabled = false;
        }

        NetworkCharacterBehaviourLocal[] localSampleBehaviours =
            _character.GetComponentsInChildren<
                NetworkCharacterBehaviourLocal
            >(true);

        foreach (NetworkCharacterBehaviourLocal localSampleBehaviour in
                 localSampleBehaviours)
        {
            localSampleBehaviour.enabled = false;
        }

        // The Movement NetworkCharacterRetargeter is the only body driver on
        // both peers. Leaving the legacy OVR retargeter enabled on the actor
        // makes two components write the same 105 transforms.
        OVRUnityHumanoidSkeletonRetargeter[] legacyRetargeters =
            _character.GetComponentsInChildren<
                OVRUnityHumanoidSkeletonRetargeter
            >(true);

        foreach (OVRUnityHumanoidSkeletonRetargeter legacyRetargeter in
                 legacyRetargeters)
        {
            legacyRetargeter.enabled = false;
        }

        Animator[] animators =
            _character.GetComponentsInChildren<Animator>(true);

        foreach (Animator animator in animators)
        {
            animator.applyRootMotion = false;
        }
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

        bool receivedNewPose = false;

        if (_receiveCount > 0)
        {
            receivedNewPose = DeserializeNextPacket();
        }

        if (IsPacketStreamStale())
        {
            return;
        }

        if (!_applyData || !_dataIsValid)
        {
            return;
        }

        // Without interpolation, _bodyPose/_facePose already contain the
        // decoded snapshot. Reapplying that same snapshot on every rendered
        // frame creates unnecessary Transform/SkinnedMesh work and continues
        // forever after the sender stops. Apply direct poses exactly once.
        if (!_networkCharacterRetargeter.UseInterpolation &&
            !receivedNewPose)
        {
            return;
        }

        if (ReadBodyData(renderTime))
        {
            if (!ApplyBodyPoseSafely())
            {
                return;
            }
        }

        if (_configuredFaceShapeCount > 0 && ReadFaceData(renderTime))
        {
            ApplyFacePoseSafely();
        }
    }

    private bool DeserializeNextPacket()
    {
        int slotIndex = _receiveReadIndex;
        int dataLength = _receiveLengths[slotIndex];

        _receiveLengths[slotIndex] = 0;
        _receiveReadIndex =
            (_receiveReadIndex + 1) % _receiveSlots.Length;
        _receiveCount--;

        if (dataLength <= 0)
        {
            return false;
        }

        // GetSubArray creates a non-owning view. The persistent owner remains
        // allocated in the ring and is reused by the next received packet.
        NativeArray<byte> data =
            _receiveSlots[slotIndex].GetSubArray(0, dataLength);

        int stageToken = RuntimeDiagnosticsFileLogger.BeginCriticalStage(
            "MOVEMENT_DESERIALIZE"
        );

        try
        {
            bool success = DeserializeSkeletonAndFace(
                _networkCharacterRetargeter.RetargetingHandle,
                data,
                SERIALIZATION_VERSION_CURRENT,
                out double snapshotTimestamp,
                out _,
                out int ack,
                ref _bodyPose,
                ref _facePose
            );

            if (!success)
            {
                _dataIsValid = false;
                return false;
            }

            if (_configuredFaceShapeCount > 0)
            {
                _networkCharacterRetargeter.DeNormalizeFaceValues(
                    ref _facePose
                );
            }

            if (!ValidateReceivedPose(snapshotTimestamp))
            {
                _dataIsValid = false;
                return false;
            }

            _dataIsValid = true;
            _latestSnapshotTimestamp = snapshotTimestamp;
            _loggedDeserializeFailure = false;
            _loggedInvalidPose = false;
            _loggedApplyFailure = false;

            if (_packetStreamIsStale)
            {
                _packetStreamIsStale = false;
                Debug.Log(
                    "ActorMovementNetworkHandler: PACKET_STALE_EXIT " +
                    $"snapshotTime={_latestSnapshotTimestamp:F3}.",
                    this
                );
            }

            SendAck(ack);
            return true;
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

            return false;
        }
        finally
        {
            RuntimeDiagnosticsFileLogger.EndCriticalStage(stageToken);
        }
    }

    private bool IsPacketStreamStale()
    {
        if (!_enableStalePacketProtection || !_hasReceivedPacket)
        {
            return false;
        }

        float packetAge =
            Time.realtimeSinceStartup - _lastPacketArrivalRealtime;

        if (packetAge <= _stalePacketTimeoutSeconds)
        {
            return false;
        }

        _dataIsValid = false;

        if (!_packetStreamIsStale)
        {
            _packetStreamIsStale = true;
            Debug.LogWarning(
                "ActorMovementNetworkHandler: PACKET_STALE_ENTER " +
                $"age={packetAge:F3}s timeout=" +
                $"{_stalePacketTimeoutSeconds:F3}s. Pose application " +
                "stopped and a full snapshot was requested.",
                this
            );

            // -1 makes the sender abandon its delta baseline and produce a
            // complete 105-joint/31-shape recovery snapshot.
            SendAck(-1);
        }

        return true;
    }

    private bool ValidateReceivedPose(double snapshotTimestamp)
    {
        if (!_validateReceivedPose)
        {
            return true;
        }

        if (double.IsNaN(snapshotTimestamp) ||
            double.IsInfinity(snapshotTimestamp))
        {
            return LogInvalidPose("snapshot timestamp is not finite");
        }

        for (int i = 0; i < _bodyPose.Length; i++)
        {
            NativeTransform pose = _bodyPose[i];

            if (!IsFinite(pose.Position) ||
                !IsFinite(pose.Orientation) ||
                !IsFinite(pose.Scale))
            {
                return LogInvalidPose(
                    $"joint {i} contains NaN or Infinity"
                );
            }

            float orientationMagnitudeSquared =
                pose.Orientation.x * pose.Orientation.x +
                pose.Orientation.y * pose.Orientation.y +
                pose.Orientation.z * pose.Orientation.z +
                pose.Orientation.w * pose.Orientation.w;

            if (orientationMagnitudeSquared < 0.000001f ||
                orientationMagnitudeSquared > 100f)
            {
                return LogInvalidPose(
                    $"joint {i} has an unsafe quaternion magnitude " +
                    $"({orientationMagnitudeSquared:F6})"
                );
            }

            if (MaxAbsoluteComponent(pose.Position) > 1000f ||
                MaxAbsoluteComponent(pose.Scale) > 1000f)
            {
                return LogInvalidPose(
                    $"joint {i} exceeds the transform safety range"
                );
            }
        }

        for (int i = 0; i < _facePose.Length; i++)
        {
            float value = _facePose[i];

            if (float.IsNaN(value) ||
                float.IsInfinity(value) ||
                Mathf.Abs(value) > 10000f)
            {
                return LogInvalidPose(
                    $"face shape {i} contains an unsafe value ({value})"
                );
            }
        }

        return true;
    }

    private bool LogInvalidPose(string reason)
    {
        if (!_loggedInvalidPose)
        {
            Debug.LogError(
                "ActorMovementNetworkHandler: INVALID_POSE packet rejected. " +
                reason + ".",
                this
            );
            _loggedInvalidPose = true;
        }

        return false;
    }

    private bool ApplyBodyPoseSafely()
    {
        int stageToken = RuntimeDiagnosticsFileLogger.BeginCriticalStage(
            "MOVEMENT_APPLY_BODY"
        );

        try
        {
            _networkCharacterRetargeter.ApplyBodyPose(
                _bodyPose,
                Meta.XR.Movement.Retargeting.JointType.NoWorldSpace
            );

            _networkCharacterRetargeter.SetDebugPose(_bodyPose);
            return true;
        }
        catch (Exception exception)
        {
            LogApplyFailure("body", exception);
            return false;
        }
        finally
        {
            RuntimeDiagnosticsFileLogger.EndCriticalStage(stageToken);
        }
    }

    private bool ApplyFacePoseSafely()
    {
        int stageToken = RuntimeDiagnosticsFileLogger.BeginCriticalStage(
            "MOVEMENT_APPLY_FACE"
        );

        try
        {
            _networkCharacterRetargeter.ApplyFacePose(_facePose);
            return true;
        }
        catch (Exception exception)
        {
            LogApplyFailure("face", exception);
            return false;
        }
        finally
        {
            RuntimeDiagnosticsFileLogger.EndCriticalStage(stageToken);
        }
    }

    private void LogApplyFailure(string poseType, Exception exception)
    {
        _dataIsValid = false;

        if (_loggedApplyFailure)
        {
            return;
        }

        Debug.LogError(
            "ActorMovementNetworkHandler: APPLY_POSE_FAILED " +
            $"type={poseType}. {exception}",
            this
        );
        _loggedApplyFailure = true;
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) &&
               IsFinite(value.y) &&
               IsFinite(value.z);
    }

    private static bool IsFinite(Quaternion value)
    {
        return IsFinite(value.x) &&
               IsFinite(value.y) &&
               IsFinite(value.z) &&
               IsFinite(value.w);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static float MaxAbsoluteComponent(Vector3 value)
    {
        return Mathf.Max(
            Mathf.Abs(value.x),
            Mathf.Abs(value.y),
            Mathf.Abs(value.z)
        );
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

        int stageToken = RuntimeDiagnosticsFileLogger.BeginCriticalStage(
            "MOVEMENT_INTERPOLATE_BODY"
        );

        try
        {
            return GetInterpolatedSkeleton(
                _networkCharacterRetargeter.RetargetingHandle,
                SkeletonType.TargetSkeleton,
                ref _bodyPose,
                renderTime
            );
        }
        finally
        {
            RuntimeDiagnosticsFileLogger.EndCriticalStage(stageToken);
        }
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

        int stageToken = RuntimeDiagnosticsFileLogger.BeginCriticalStage(
            "MOVEMENT_INTERPOLATE_FACE"
        );

        bool interpolationSucceeded;

        try
        {
            interpolationSucceeded = GetInterpolatedFace(
                _networkCharacterRetargeter.RetargetingHandle,
                SkeletonType.TargetSkeleton,
                ref _facePose,
                renderTime
            );
        }
        finally
        {
            RuntimeDiagnosticsFileLogger.EndCriticalStage(stageToken);
        }

        if (!interpolationSucceeded)
        {
            return false;
        }

        _networkCharacterRetargeter.DeNormalizeFaceValues(ref _facePose);
        return true;
    }

    private bool IsLocalTrackingReady()
    {
        return _networkCharacterRetargeter != null &&
               _networkCharacterRetargeter.IsValid &&
               _networkCharacterRetargeter.SkeletonRetargeter != null &&
               _networkCharacterRetargeter.SkeletonRetargeter.IsInitialized &&
               _networkCharacterRetargeter.SkeletonRetargeter.AppliedPose;
    }

    private void UpdateLocalTrackingState()
    {
        bool trackingIsValid = IsLocalTrackingReady();

        if (_hasObservedLocalTrackingState &&
            trackingIsValid == _lastLocalTrackingState)
        {
            return;
        }

        bool hadObservedTrackingState = _hasObservedLocalTrackingState;

        _hasObservedLocalTrackingState = true;
        _lastLocalTrackingState = trackingIsValid;

        if (trackingIsValid)
        {
            _forceFullSnapshot = true;
            Debug.Log(
                "ActorMovementNetworkHandler: " +
                (hadObservedTrackingState
                    ? "TRACKING_RECOVERED"
                    : "TRACKING_VALID") +
                ". A complete recovery snapshot will be sent.",
                this
            );
        }
        else
        {
            Debug.LogWarning(
                "ActorMovementNetworkHandler: TRACKING_LOST. Movement " +
                "snapshot transmission is paused until tracking recovers.",
                this
            );
        }
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
        _lastSerializedPacketWasFullSnapshot = false;

        if (!_setupComplete ||
            _networkCharacterRetargeter.RetargetingHandle == INVALID_HANDLE)
        {
            return;
        }

        if (_forceFullSnapshot ||
            ShouldSyncData ||
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

            _lastSerializedPacketWasFullSnapshot =
                _dataIsValid && lastAck == -1;
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
                $"receive-discarded " +
                $"{_callbackOnlyDiscardedPacketsInWindow}, " +
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
        _callbackOnlyDiscardedPacketsInWindow = 0;
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
        _loggedInvalidPose = false;
        _loggedApplyFailure = false;
        _loggedCallbackOnlySetup = false;
        _loggedCallbackOnlyPacket = false;
        _hasReceivedPacket = false;
        _packetStreamIsStale = false;
        _lastPacketArrivalRealtime = 0f;
        _latestSnapshotTimestamp = 0d;
        _hasObservedLocalTrackingState = false;
        _lastLocalTrackingState = false;
        _forceFullSnapshot = true;
        _lastSerializedPacketWasFullSnapshot = false;

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
