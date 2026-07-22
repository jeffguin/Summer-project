using Fusion;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
[RequireComponent(typeof(Rigidbody))]
public class NetworkPhysicalGrabbable : NetworkBehaviour
{
    public enum GrabRole
    {
        None = 0,
        Actor = 1,
        Audience = 2
    }

    [Header("Physics")]
    [SerializeField] private Rigidbody rb;

    [Header("Interaction Audio")]
    [Tooltip("可选。配置后，抓取和释放成功时会由 State Authority 向双方广播音效。")]
    [SerializeField] private NetworkInteractionAudio interactionAudio;

    [Header("Grab Follow Settings")]
    [SerializeField] private float followSpeed = 25f;
    [SerializeField] private float rotateSpeed = 25f;

    [Header("Release Settings")]
    [SerializeField] private bool enableGravityOnRelease = true;

    [Tooltip("释放后短暂冷却，防止同一帧或相邻帧重复 grab。调试阶段可设为 0 或 0.05。")]
    [SerializeField] private float releaseCooldown = 0.05f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;
    [SerializeField] private bool debugMoveLog = false;

    [Networked] public NetworkBool IsGrabbed { get; private set; }
    [Networked] public PlayerRef GrabbedByPlayer { get; private set; }
    [Networked] public int GrabbedByRoleValue { get; private set; }

    [Networked] public Vector3 TargetPosition { get; private set; }
    [Networked] public Quaternion TargetRotation { get; private set; }

    [Networked] private TickTimer ReleaseCooldownTimer { get; set; }

    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private Vector3 estimatedVelocity;
    private Vector3 estimatedAngularVelocity;

    private int moveDebugCounter = 0;

    public GrabRole CurrentGrabRole => (GrabRole)GrabbedByRoleValue;

    private void Awake()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }

        if (interactionAudio == null)
        {
            interactionAudio = GetComponent<NetworkInteractionAudio>();
        }
    }

    public override void Spawned()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }

        if (interactionAudio == null)
        {
            interactionAudio = GetComponent<NetworkInteractionAudio>();
        }

        TargetPosition = transform.position;
        TargetRotation = transform.rotation;

        lastPosition = transform.position;
        lastRotation = transform.rotation;

        estimatedVelocity = Vector3.zero;
        estimatedAngularVelocity = Vector3.zero;

        if (Object.HasStateAuthority)
        {
            IsGrabbed = false;
            GrabbedByPlayer = default;
            GrabbedByRoleValue = (int)GrabRole.None;
            ReleaseCooldownTimer = TickTimer.None;

            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        DebugMessage(
            $"Spawned. HasStateAuthority={Object.HasStateAuthority}, " +
            $"HasInputAuthority={Object.HasInputAuthority}, " +
            $"Position={transform.position}"
        );
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        ClearExpiredCooldownIfNeeded();

        if (IsGrabbed)
        {
            MoveTowardsTarget();
            EstimateVelocity();
        }
    }

    private void ClearExpiredCooldownIfNeeded()
    {
        if (ReleaseCooldownTimer.IsRunning && ReleaseCooldownTimer.Expired(Runner))
        {
            ReleaseCooldownTimer = TickTimer.None;
            DebugMessage("Release cooldown expired and cleared.");
        }
    }

    private void MoveTowardsTarget()
    {
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;

            Vector3 nextPosition = Vector3.Lerp(
                rb.position,
                TargetPosition,
                Runner.DeltaTime * followSpeed
            );

            Quaternion nextRotation = Quaternion.Slerp(
                rb.rotation,
                TargetRotation,
                Runner.DeltaTime * rotateSpeed
            );

            rb.MovePosition(nextPosition);
            rb.MoveRotation(nextRotation);
        }
        else
        {
            transform.position = Vector3.Lerp(
                transform.position,
                TargetPosition,
                Runner.DeltaTime * followSpeed
            );

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                TargetRotation,
                Runner.DeltaTime * rotateSpeed
            );
        }

        if (debugMoveLog)
        {
            moveDebugCounter++;

            if (moveDebugCounter % 30 == 0)
            {
                DebugMessage(
                    $"Moving. CurrentPosition={transform.position}, " +
                    $"TargetPosition={TargetPosition}, IsGrabbed={IsGrabbed}, " +
                    $"Owner={GrabbedByPlayer}, Role={CurrentGrabRole}"
                );
            }
        }
    }

    private void EstimateVelocity()
    {
        float dt = Runner.DeltaTime;

        if (dt <= 0f)
            return;

        estimatedVelocity = (transform.position - lastPosition) / dt;

        Quaternion deltaRotation = transform.rotation * Quaternion.Inverse(lastRotation);
        deltaRotation.ToAngleAxis(out float angle, out Vector3 axis);

        if (angle > 180f)
        {
            angle -= 360f;
        }

        if (axis.sqrMagnitude > 0.0001f)
        {
            estimatedAngularVelocity = axis.normalized * angle * Mathf.Deg2Rad / dt;
        }
        else
        {
            estimatedAngularVelocity = Vector3.zero;
        }

        lastPosition = transform.position;
        lastRotation = transform.rotation;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestGrab(PlayerRef requester, int requesterRoleValue)
    {
        GrabRole requesterRole = (GrabRole)requesterRoleValue;

        DebugMessage(
            $"RPC_RequestGrab received. " +
            $"Requester={requester}, Role={requesterRole}, " +
            $"IsGrabbed={IsGrabbed}, CurrentOwner={GrabbedByPlayer}, CurrentRole={CurrentGrabRole}, " +
            $"CooldownRunning={ReleaseCooldownTimer.IsRunning}, " +
            $"CooldownExpiredOrNotRunning={ReleaseCooldownTimer.ExpiredOrNotRunning(Runner)}"
        );

        if (!ReleaseCooldownTimer.ExpiredOrNotRunning(Runner))
        {
            DebugMessage("Grab rejected because release cooldown is still active.");
            return;
        }

        if (IsGrabbed)
        {
            DebugMessage(
                $"Grab rejected. Already grabbed by {GrabbedByPlayer}, Role={CurrentGrabRole}"
            );
            return;
        }

        IsGrabbed = true;
        GrabbedByPlayer = requester;
        GrabbedByRoleValue = requesterRoleValue;

        TargetPosition = transform.position;
        TargetRotation = transform.rotation;

        lastPosition = transform.position;
        lastRotation = transform.rotation;

        estimatedVelocity = Vector3.zero;
        estimatedAngularVelocity = Vector3.zero;

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        DebugMessage(
            $"Grab accepted. Owner={GrabbedByPlayer}, Role={CurrentGrabRole}"
        );

        if (interactionAudio != null)
        {
            interactionAudio.PlayFromStateAuthority(
                NetworkInteractionAudio.InteractionSoundType.Grab
            );
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_UpdateGrabTarget(
        PlayerRef requester,
        int requesterRoleValue,
        Vector3 targetPosition,
        Quaternion targetRotation)
    {
        if (!IsGrabbed)
        {
            DebugMessage(
                $"Target update rejected because object is not grabbed. " +
                $"Requester={requester}, Role={(GrabRole)requesterRoleValue}"
            );
            return;
        }

        if (GrabbedByPlayer != requester)
        {
            DebugMessage(
                $"Target update rejected. Requester={requester}, Owner={GrabbedByPlayer}"
            );
            return;
        }

        if (GrabbedByRoleValue != requesterRoleValue)
        {
            DebugMessage(
                $"Target update rejected. Role mismatch. " +
                $"RequesterRole={(GrabRole)requesterRoleValue}, CurrentRole={CurrentGrabRole}"
            );
            return;
        }

        TargetPosition = targetPosition;
        TargetRotation = targetRotation;

        if (debugMoveLog)
        {
            DebugMessage(
                $"Target updated. Requester={requester}, Role={(GrabRole)requesterRoleValue}, " +
                $"TargetPosition={TargetPosition}, TargetRotation={TargetRotation.eulerAngles}"
            );
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestRelease(PlayerRef requester, int requesterRoleValue)
    {
        DebugMessage(
            $"RPC_RequestRelease received. " +
            $"Requester={requester}, Role={(GrabRole)requesterRoleValue}, " +
            $"IsGrabbed={IsGrabbed}, Owner={GrabbedByPlayer}, CurrentRole={CurrentGrabRole}"
        );

        if (!IsGrabbed)
        {
            DebugMessage("Release ignored because object is not grabbed.");
            return;
        }

        if (GrabbedByPlayer != requester)
        {
            DebugMessage(
                $"Release rejected. Requester={requester}, Owner={GrabbedByPlayer}"
            );
            return;
        }

        if (GrabbedByRoleValue != requesterRoleValue)
        {
            DebugMessage(
                $"Release rejected. Role mismatch. " +
                $"RequesterRole={(GrabRole)requesterRoleValue}, CurrentRole={CurrentGrabRole}"
            );
            return;
        }

        IsGrabbed = false;
        GrabbedByPlayer = default;
        GrabbedByRoleValue = (int)GrabRole.None;

        if (releaseCooldown > 0f)
        {
            ReleaseCooldownTimer = TickTimer.CreateFromSeconds(Runner, releaseCooldown);
        }
        else
        {
            ReleaseCooldownTimer = TickTimer.None;
        }

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = enableGravityOnRelease;
            rb.linearVelocity = estimatedVelocity;
            rb.angularVelocity = estimatedAngularVelocity;
        }

        DebugMessage(
            $"Released successfully. Cooldown={releaseCooldown}, " +
            $"Velocity={estimatedVelocity}, AngularVelocity={estimatedAngularVelocity}"
        );

        if (interactionAudio != null)
        {
            interactionAudio.PlayFromStateAuthority(
                NetworkInteractionAudio.InteractionSoundType.Release
            );
        }
    }

    public bool IsControlledBy(PlayerRef player, GrabRole role)
    {
        return IsGrabbed &&
               GrabbedByPlayer == player &&
               CurrentGrabRole == role;
    }

    public bool CanBeGrabbed()
    {
        return !IsGrabbed && ReleaseCooldownTimer.ExpiredOrNotRunning(Runner);
    }

    private void DebugMessage(string message)
    {
        if (!debugLog)
            return;

        Debug.Log($"[NetworkPhysicalGrabbable] {gameObject.name}: {message}");
    }
}
