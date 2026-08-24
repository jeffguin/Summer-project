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

    public enum GrabHand
    {
        None = 0,
        Left = 1,
        Right = 2
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

    [Header("Reset Settings")]
    [Tooltip("Reset All 后短暂阻止重新抓取，避免重置前的残留输入在下一帧重新占用物体。")]
    [SerializeField] private float resetCooldown = 0.25f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;
    [SerializeField] private bool debugMoveLog = false;

    [Networked] public NetworkBool IsGrabbed { get; private set; }
    [Networked] public PlayerRef GrabbedByPlayer { get; private set; }
    [Networked] public int GrabbedByRoleValue { get; private set; }
    [Networked] public int GrabbedHandValue { get; private set; }

    [Networked]
    public NetworkBool UsesAvatarHandAttachment { get; private set; }

    [Networked] private Vector3 AvatarHandPositionOffset { get; set; }
    [Networked] private Quaternion AvatarHandRotationOffset { get; set; }
    [Networked] private NetworkBool AvatarHandOffsetIsValid { get; set; }
    [Networked] private NetworkBool HasReceivedGrabTarget { get; set; }

    [Networked] public Vector3 TargetPosition { get; private set; }
    [Networked] public Quaternion TargetRotation { get; private set; }

    [Networked] private TickTimer ReleaseCooldownTimer { get; set; }

    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private Vector3 estimatedVelocity;
    private Vector3 estimatedAngularVelocity;

    private NetworkTransform networkTransform;
    private int moveDebugCounter = 0;

    public GrabRole CurrentGrabRole => (GrabRole)GrabbedByRoleValue;
    public GrabHand CurrentGrabHand => (GrabHand)GrabbedHandValue;

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

        if (networkTransform == null)
        {
            networkTransform = GetComponent<NetworkTransform>();
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

        if (networkTransform == null)
        {
            networkTransform = GetComponent<NetworkTransform>();
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
            GrabbedHandValue = (int)GrabHand.None;
            UsesAvatarHandAttachment = false;
            AvatarHandPositionOffset = Vector3.zero;
            AvatarHandRotationOffset = Quaternion.identity;
            AvatarHandOffsetIsValid = false;
            HasReceivedGrabTarget = false;
            ReleaseCooldownTimer = TickTimer.None;

            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
        else if (rb != null)
        {
            // Only State Authority simulates the held/released rigidbody.
            // Proxies are driven by NetworkTransform and the late wrist
            // correction below.
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
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
            UpdateTargetFromAvatarHand();
            MoveTowardsTarget();
            EstimateVelocity();
        }
    }

    private void LateUpdate()
    {
        if (Object == null ||
            !Object.IsValid ||
            Object.HasStateAuthority ||
            !IsGrabbed ||
            !UsesAvatarHandAttachment ||
            !AvatarHandOffsetIsValid)
        {
            return;
        }

        if (TryGetAvatarHandAttachmentPose(
                out Vector3 position,
                out Quaternion rotation))
        {
            // NetworkTransform remains authoritative for the released pose and
            // physics state. On proxies, this late visual correction keeps the
            // held object locked to the same interpolated wrist as the avatar.
            transform.SetPositionAndRotation(position, rotation);
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
    public void RPC_RequestGrab(
        PlayerRef requester,
        int requesterRoleValue,
        int requesterHandValue)
    {
        GrabRole requesterRole = (GrabRole)requesterRoleValue;
        GrabHand requesterHand = SanitizeGrabHand(requesterHandValue);

        DebugMessage(
            $"RPC_RequestGrab received. " +
            $"Requester={requester}, Role={requesterRole}, Hand={requesterHand}, " +
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

        // Socket ownership lives on State Authority. Detach here so every
        // interaction path (OVR hand/controller and SteamVR ray) releases the
        // board slot before the network grab starts.
        TicTacToeSocketableObject socketable =
            GetComponent<TicTacToeSocketableObject>();

        if (socketable != null)
        {
            socketable.RemoveFromSocket();
        }

        IsGrabbed = true;
        GrabbedByPlayer = requester;
        GrabbedByRoleValue = requesterRoleValue;
        GrabbedHandValue = (int)requesterHand;

        UsesAvatarHandAttachment =
            requesterRole == GrabRole.Actor &&
            requesterHand != GrabHand.None;
        AvatarHandPositionOffset = Vector3.zero;
        AvatarHandRotationOffset = Quaternion.identity;
        AvatarHandOffsetIsValid = false;
        HasReceivedGrabTarget = false;

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
            $"Grab accepted. Owner={GrabbedByPlayer}, Role={CurrentGrabRole}, " +
            $"Hand={CurrentGrabHand}, " +
            $"AvatarAttachment={UsesAvatarHandAttachment}"
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

        HasReceivedGrabTarget = true;

        if (UsesAvatarHandAttachment &&
            TryInitializeAvatarHandOffset(targetPosition, targetRotation))
        {
            UpdateTargetFromAvatarHand();
        }
        else
        {
            TargetPosition = targetPosition;
            TargetRotation = targetRotation;
        }

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
        GrabbedHandValue = (int)GrabHand.None;
        UsesAvatarHandAttachment = false;
        AvatarHandPositionOffset = Vector3.zero;
        AvatarHandRotationOffset = Quaternion.identity;
        AvatarHandOffsetIsValid = false;
        HasReceivedGrabTarget = false;

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

    /// <summary>
    /// 由 State Authority 强制结束当前抓取，并把原 NetworkObject Teleport 回生成位姿。
    /// 此操作保留 NetworkId，不执行 Despawn / Respawn。
    /// </summary>
    public bool ForceResetToPose(Vector3 resetPosition, Quaternion resetRotation)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            Debug.LogWarning(
                $"[NetworkPhysicalGrabbable] {gameObject.name}: " +
                "ForceResetToPose ignored because this peer is not State Authority."
            );
            return false;
        }

        IsGrabbed = false;
        GrabbedByPlayer = default;
        GrabbedByRoleValue = (int)GrabRole.None;
        GrabbedHandValue = (int)GrabHand.None;
        UsesAvatarHandAttachment = false;
        AvatarHandPositionOffset = Vector3.zero;
        AvatarHandRotationOffset = Quaternion.identity;
        AvatarHandOffsetIsValid = false;
        HasReceivedGrabTarget = false;

        TargetPosition = resetPosition;
        TargetRotation = resetRotation;

        ReleaseCooldownTimer = resetCooldown > 0f
            ? TickTimer.CreateFromSeconds(Runner, resetCooldown)
            : TickTimer.None;

        lastPosition = resetPosition;
        lastRotation = resetRotation;
        estimatedVelocity = Vector3.zero;
        estimatedAngularVelocity = Vector3.zero;
        moveDebugCounter = 0;

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (networkTransform == null)
        {
            networkTransform = GetComponent<NetworkTransform>();
        }

        if (networkTransform != null)
        {
            networkTransform.Teleport(resetPosition, resetRotation);
        }
        else
        {
            transform.SetPositionAndRotation(resetPosition, resetRotation);
        }

        if (rb != null)
        {
            rb.position = resetPosition;
            rb.rotation = resetRotation;
        }

        DebugMessage(
            $"Force reset completed. Position={resetPosition}, " +
            $"Rotation={resetRotation.eulerAngles}, Cooldown={resetCooldown}"
        );

        return true;
    }

    public bool IsControlledBy(PlayerRef player, GrabRole role)
    {
        return IsGrabbed &&
               GrabbedByPlayer == player &&
               CurrentGrabRole == role;
    }

    private static GrabHand SanitizeGrabHand(int handValue)
    {
        return handValue switch
        {
            (int)GrabHand.Left => GrabHand.Left,
            (int)GrabHand.Right => GrabHand.Right,
            _ => GrabHand.None
        };
    }

    private bool TryGetAvatarHandAnchor(out Transform anchor)
    {
        anchor = null;

        return UsesAvatarHandAttachment &&
               CurrentGrabRole == GrabRole.Actor &&
               CurrentGrabHand != GrabHand.None &&
               AvatarHandAnchorProvider.TryGetAnchor(
                   GrabbedByPlayer,
                   CurrentGrabHand,
                   out anchor
               );
    }

    private bool TryInitializeAvatarHandOffset(
        Vector3 objectPosition,
        Quaternion objectRotation)
    {
        if (AvatarHandOffsetIsValid)
        {
            return true;
        }

        if (!TryGetAvatarHandAnchor(out Transform anchor))
        {
            return false;
        }

        // Use rotation-only offsets so the avatar's non-uniform wrist/anchor
        // scale cannot distort or resize held network objects.
        AvatarHandPositionOffset =
            Quaternion.Inverse(anchor.rotation) *
            (objectPosition - anchor.position);
        AvatarHandRotationOffset =
            (Quaternion.Inverse(anchor.rotation) * objectRotation).normalized;
        AvatarHandOffsetIsValid = true;
        return true;
    }

    private bool TryGetAvatarHandAttachmentPose(
        out Vector3 position,
        out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;

        if (!AvatarHandOffsetIsValid ||
            !TryGetAvatarHandAnchor(out Transform anchor))
        {
            return false;
        }

        position =
            anchor.position + anchor.rotation * AvatarHandPositionOffset;
        rotation =
            (anchor.rotation * AvatarHandRotationOffset).normalized;
        return true;
    }

    private void UpdateTargetFromAvatarHand()
    {
        if (!UsesAvatarHandAttachment)
        {
            return;
        }

        if (!HasReceivedGrabTarget)
        {
            return;
        }

        if (!AvatarHandOffsetIsValid &&
            !TryInitializeAvatarHandOffset(TargetPosition, TargetRotation))
        {
            return;
        }

        if (TryGetAvatarHandAttachmentPose(
                out Vector3 position,
                out Quaternion rotation))
        {
            TargetPosition = position;
            TargetRotation = rotation;
        }
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
