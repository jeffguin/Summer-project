using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class ActorNetworkRootDriver : NetworkBehaviour
{
    [Header("Runtime References")]
    [Tooltip("Actor-side tracking-space root. BasicSpawner assigns the OVRCameraRig root at runtime.")]
    [SerializeField]
    private Transform _localRootSource;

    [Tooltip("Stage-space origin for the actor. BasicSpawner assigns the actor spawn point at runtime.")]
    [SerializeField]
    private Transform _stageSpawnPoint;

    [Header("Calibration")]
    [Tooltip("Only use yaw when mapping tracking-space rotation into stage space.")]
    [SerializeField]
    private bool _useYawOnly = true;

    [Tooltip("Keep the network root at the calibrated stage height.")]
    [SerializeField]
    private bool _lockHeightToSpawnPoint = true;

    [Tooltip("Additional vertical correction for the avatar feet.")]
    [SerializeField]
    private float _heightOffset = 0f;

    private Quaternion _calibrationRotation = Quaternion.identity;
    private Vector3 _calibrationPosition;
    private float _calibratedWorldHeight;
    private bool _isCalibrated;

    public bool IsCalibrated => _isCalibrated;

    public void SetCalibrationReferences(
        Transform localRootSource,
        Transform stageSpawnPoint)
    {
        _localRootSource = localRootSource;
        _stageSpawnPoint = stageSpawnPoint;
    }

    public bool Calibrate()
    {
        if (Object != null && !Object.HasStateAuthority)
        {
            Debug.LogWarning(
                "ActorNetworkRootDriver: Calibration ignored because this peer " +
                "does not have State Authority."
            );
            return false;
        }

        if (_localRootSource == null)
        {
            Debug.LogError(
                "ActorNetworkRootDriver: Local Root Source is missing. " +
                "Assign the OVRCameraRig root through BasicSpawner."
            );
            return false;
        }

        if (_stageSpawnPoint == null)
        {
            Debug.LogError(
                "ActorNetworkRootDriver: Stage Spawn Point is missing."
            );
            return false;
        }

        Quaternion sourceRotation =
            GetCalibrationRotation(_localRootSource.rotation);

        Quaternion targetRotation =
            GetCalibrationRotation(_stageSpawnPoint.rotation);

        _calibrationRotation =
            targetRotation * Quaternion.Inverse(sourceRotation);

        _calibrationPosition =
            _stageSpawnPoint.position -
            _calibrationRotation * _localRootSource.position;

        _calibratedWorldHeight =
            _stageSpawnPoint.position.y + _heightOffset;

        _isCalibrated = true;

        ApplyCalibratedTransform();

        Debug.Log(
            "ActorNetworkRootDriver: Actor calibrated successfully." +
            $"\nSource Position: {_localRootSource.position:F3}" +
            $"\nSource Rotation: {_localRootSource.eulerAngles:F2}" +
            $"\nStage Position: {_stageSpawnPoint.position:F3}" +
            $"\nStage Rotation: {_stageSpawnPoint.eulerAngles:F2}" +
            $"\nHeight Offset: {_heightOffset:F3}"
        );

        return true;
    }

    public void ClearCalibration()
    {
        if (Object != null && !Object.HasStateAuthority)
            return;

        _isCalibrated = false;

        Debug.Log(
            "ActorNetworkRootDriver: Calibration cleared."
        );
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority ||
            !_isCalibrated ||
            _localRootSource == null)
        {
            return;
        }

        ApplyCalibratedTransform();
    }

    private void ApplyCalibratedTransform()
    {
        Quaternion sourceRotation =
            GetCalibrationRotation(_localRootSource.rotation);

        Quaternion targetRotation =
            _calibrationRotation * sourceRotation;

        Vector3 targetPosition =
            _calibrationPosition +
            _calibrationRotation * _localRootSource.position;

        if (_lockHeightToSpawnPoint)
        {
            targetPosition.y = _calibratedWorldHeight;
        }
        else
        {
            targetPosition.y += _heightOffset;
        }

        transform.SetPositionAndRotation(
            targetPosition,
            targetRotation
        );
    }

    private Quaternion GetCalibrationRotation(Quaternion rotation)
    {
        if (!_useYawOnly)
            return rotation;

        Vector3 forward = rotation * Vector3.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
            return Quaternion.identity;

        return Quaternion.LookRotation(
            forward.normalized,
            Vector3.up
        );
    }
}
