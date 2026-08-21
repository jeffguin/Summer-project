#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

using Fusion;
using UnityEngine;
using UnityEngine.XR;
using Valve.VR;

public class ViveRayNetworkPhysicalGrabAdapter : MonoBehaviour
{
    [Header("Role")]
    [SerializeField]
    private NetworkPhysicalGrabbable.GrabRole grabRole =
        NetworkPhysicalGrabbable.GrabRole.Audience;

    [Header("Ray Source")]
    [SerializeField] private Transform rayOrigin;
    [Tooltip("用于忽略手柄自身 Collider。留空时使用当前物体。")]
    [SerializeField] private Transform controllerRoot;
    [Tooltip("在现有 RayOrigin 朝向上增加的可调欧拉角偏移。")]
    [SerializeField] private Vector3 rayRotationOffset;
    [Tooltip("射线起点相对 RayOrigin 的本地位置偏移。")]
    [SerializeField] private Vector3 rayStartOffset;
    [SerializeField] private float maxDistance = 5f;
    [SerializeField] private LayerMask interactableLayers = ~0;
    [Tooltip("真正会遮挡交互射线的 Layer，例如 Blockers。")]
    [SerializeField] private LayerMask blockingLayers;

    [Header("SteamVR Input")]
    [SerializeField] private SteamVR_Action_Boolean grabAction;
    [SerializeField] private SteamVR_Input_Sources inputSource = SteamVR_Input_Sources.RightHand;
    [Tooltip("启动时主动激活 GrabAction 所在的 SteamVR Action Set。")]
    [SerializeField] private bool activateActionSetOnStart = true;
    [Tooltip("SteamVR 动作未绑定或未激活时，使用 Unity XR 的右扳机作为兜底。")]
    [SerializeField] private bool enableUnityXrTriggerFallback = true;
    [Range(0.1f, 1f)]
    [SerializeField] private float xrTriggerThreshold = 0.75f;

    [Header("Ray Visual")]
    [SerializeField] private LineRenderer lineRenderer;
    [SerializeField] private Material idleRayMaterial;
    [SerializeField] private Material hoverRayMaterial;
    [SerializeField] private Material grabRayMaterial;
    [SerializeField] private float rayWidth = 0.01f;

    [Header("Grab Settings")]
    [SerializeField] private bool keepInitialRotation = true;
    [SerializeField] private float defaultGrabDistance = 1.5f;
    [SerializeField] private float targetSendRate = 30f;
    [Tooltip("等待 State Authority 确认抓取的最长时间。")]
    [SerializeField] private float grabConfirmationTimeout = 2f;

    [Header("Input Mode")]
    [Tooltip("开启后：第一次 click 抓取，第二次 click 释放。适合 SteamVR Boolean Click 输入。")]
    [SerializeField] private bool toggleGrabMode = true;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;
    [SerializeField] private bool drawDebugRay = false;
    [SerializeField] private bool debugHoverLog = false;

    private NetworkRunner runner;

    private NetworkPhysicalGrabbable hoveredObject;
    private NetworkPhysicalGrabbable grabbedObject;
    private NetworkPhysicalGrabbable pendingGrabObject;

    private float grabbedDistance;
    private Quaternion grabbedRotationOffset;
    private float nextTargetSendTime;
    private float pendingGrabDeadline;
    private Ray currentRay;
    private Vector3 currentRayVisualEnd;
    private bool xrTriggerWasPressed;
    private bool loggedInactiveSteamVrAction;
    private bool loggedXrFallback;

    private void Start()
    {
        runner = FindFirstObjectByType<NetworkRunner>();

        if (controllerRoot == null)
            controllerRoot = transform;

        if (rayOrigin == null)
        {
            Debug.LogError("ViveRayNetworkPhysicalGrabAdapter: RayOrigin is missing.");
        }

        if (grabAction == null)
        {
            Debug.LogError("ViveRayNetworkPhysicalGrabAdapter: GrabAction is missing.");
        }
        else if (activateActionSetOnStart && grabAction.actionSet != null)
        {
            grabAction.actionSet.Activate(inputSource);
        }

        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 2;
            lineRenderer.startWidth = rayWidth;
            lineRenderer.endWidth = rayWidth;
            lineRenderer.enabled = true;
        }

        DebugMessage(
            $"Started. RunnerFound={runner != null}, Role={grabRole}, " +
            $"InputSource={inputSource}, ToggleGrabMode={toggleGrabMode}"
        );
    }

    private void Update()
    {
        if (runner == null)
        {
            runner = FindFirstObjectByType<NetworkRunner>();
        }

        EvaluateRay();
        UpdateInput();
        UpdatePendingGrab();

        // Toggle 模式下，只要已经抓住物体，就持续发送目标位置。
        // 非 Toggle 模式下，grabbedObject 也只会在按住期间存在。
        if (grabbedObject != null)
        {
            UpdateGrabTarget();
        }

        UpdateRayVisual();
    }

    private void EvaluateRay()
    {
        hoveredObject = null;

        if (rayOrigin == null)
            return;

        currentRay = BuildRay();
        currentRayVisualEnd =
            currentRay.origin + currentRay.direction * maxDistance;

        if (drawDebugRay)
        {
            Debug.DrawRay(
                currentRay.origin,
                currentRay.direction * maxDistance,
                Color.cyan
            );
        }

        if (TryFindGrabbableUnderRay(
                currentRay,
                out NetworkPhysicalGrabbable target,
                out RaycastHit hit,
                out RaycastHit blockingHit))
        {
            hoveredObject = target;
            currentRayVisualEnd = hit.point;

            if (debugHoverLog && hoveredObject != null)
            {
                DebugMessage($"Hovering: {hoveredObject.name}, HitPoint={hit.point}");
            }
        }
        else if (blockingHit.collider != null)
        {
            currentRayVisualEnd = blockingHit.point;
        }
    }

    private void UpdateInput()
    {
        if (runner == null)
        {
            DebugMessage("UpdateInput skipped because runner is null.");
            return;
        }

        ReadGrabInput(out bool grabDown, out bool grabHeld, out bool grabUp);

        if (toggleGrabMode)
        {
            if (grabDown)
            {
                if (grabbedObject == null && pendingGrabObject == null)
                {
                    DebugMessage("Toggle click: begin grab.");
                    TryBeginGrab();
                }
                else
                {
                    DebugMessage("Toggle click: release grab.");
                    EndGrab();
                }
            }

            return;
        }

        // 非 Toggle 模式：按下抓取，松开释放。
        if (grabDown)
        {
            DebugMessage("Grab action down.");
            TryBeginGrab();
        }

        if (grabHeld && grabbedObject != null)
        {
            DebugMessage($"Grab action holding. GrabbedObject={grabbedObject.name}");
        }

        if (grabUp)
        {
            DebugMessage("Grab action up.");
            EndGrab();
        }
    }

    private void ReadGrabInput(
        out bool stateDown,
        out bool state,
        out bool stateUp)
    {
        bool steamVrDown = false;
        bool steamVrState = false;
        bool steamVrUp = false;

        if (grabAction != null)
        {
            steamVrDown = grabAction.GetStateDown(inputSource);
            steamVrState = grabAction.GetState(inputSource);
            steamVrUp = grabAction.GetStateUp(inputSource);

            if (!grabAction.GetActive(inputSource) && !loggedInactiveSteamVrAction)
            {
                Debug.LogWarning(
                    "[ViveRayNetworkPhysicalGrabAdapter] SteamVR GrabAction is " +
                    $"not active for {inputSource}. Unity XR trigger fallback will be used when available."
                );
                loggedInactiveSteamVrAction = true;
            }
        }

        bool xrDown = false;
        bool xrState = false;
        bool xrUp = false;

        if (enableUnityXrTriggerFallback &&
            TryReadUnityXrTrigger(out bool xrPressed))
        {
            xrState = xrPressed;
            xrDown = xrPressed && !xrTriggerWasPressed;
            xrUp = !xrPressed && xrTriggerWasPressed;
            xrTriggerWasPressed = xrPressed;

            if (xrDown && !steamVrDown && !loggedXrFallback)
            {
                Debug.LogWarning(
                    "[ViveRayNetworkPhysicalGrabAdapter] Grab was triggered by " +
                    "the Unity XR fallback. Check the SteamVR rightconcl binding on this PC."
                );
                loggedXrFallback = true;
            }
        }
        else
        {
            xrTriggerWasPressed = false;
        }

        stateDown = steamVrDown || xrDown;
        state = steamVrState || xrState;
        stateUp = steamVrUp || xrUp;
    }

    private bool TryReadUnityXrTrigger(out bool pressed)
    {
        pressed = false;

        XRNode node;
        if (inputSource == SteamVR_Input_Sources.RightHand)
        {
            node = XRNode.RightHand;
        }
        else if (inputSource == SteamVR_Input_Sources.LeftHand)
        {
            node = XRNode.LeftHand;
        }
        else
        {
            return false;
        }

        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
            return false;

        if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerButton))
        {
            pressed = triggerButton;
            return true;
        }

        if (device.TryGetFeatureValue(CommonUsages.trigger, out float triggerValue))
        {
            pressed = triggerValue >= xrTriggerThreshold;
            return true;
        }

        return false;
    }

    private void TryBeginGrab()
    {
        NetworkPhysicalGrabbable targetObject = hoveredObject;

        // 按下 click 的那一帧 hoveredObject 可能刚好为空，
        // 所以这里重新做一次 raycast，避免“明明射线命中但抓取失败”。
        if (targetObject == null)
        {
            targetObject = FindGrabbableUnderRay(out RaycastHit hitInfo);

            if (targetObject != null)
            {
                DebugMessage(
                    $"Grab press recovered object by direct raycast. " +
                    $"Object={targetObject.name}, HitPoint={hitInfo.point}"
                );
            }
        }

        if (targetObject == null)
        {
            DebugMessage("Grab pressed but no NetworkPhysicalGrabbable was hit.");
            return;
        }

        Ray ray = BuildRay();
        grabbedDistance = Vector3.Distance(
            ray.origin,
            targetObject.transform.position
        );

        if (grabbedDistance <= 0.05f)
        {
            grabbedDistance = defaultGrabDistance;
        }

        if (keepInitialRotation)
        {
            grabbedRotationOffset =
                Quaternion.Inverse(GetRayRotation()) *
                targetObject.transform.rotation;
        }
        else
        {
            grabbedRotationOffset = Quaternion.identity;
        }

        nextTargetSendTime = 0f;
        pendingGrabObject = targetObject;
        pendingGrabDeadline = Time.time + Mathf.Max(0.1f, grabConfirmationTimeout);

        DebugMessage(
            $"Request grab. Object={pendingGrabObject.name}, " +
            $"Player={runner.LocalPlayer}, Role={grabRole}, Distance={grabbedDistance}"
        );

        pendingGrabObject.RPC_RequestGrab(
            runner.LocalPlayer,
            (int)grabRole
        );
    }

    private void UpdatePendingGrab()
    {
        if (pendingGrabObject == null || runner == null)
            return;

        if (pendingGrabObject.IsControlledBy(runner.LocalPlayer, grabRole))
        {
            grabbedObject = pendingGrabObject;
            pendingGrabObject = null;

            DebugMessage(
                $"Grab confirmed by State Authority. Object={grabbedObject.name}, " +
                $"Player={runner.LocalPlayer}, Role={grabRole}"
            );

            SendGrabTargetImmediately();
            return;
        }

        if (pendingGrabObject.IsGrabbed || Time.time >= pendingGrabDeadline)
        {
            Debug.LogWarning(
                "[ViveRayNetworkPhysicalGrabAdapter] Grab request was not accepted " +
                $"by State Authority. Object={pendingGrabObject.name}, " +
                $"IsGrabbed={pendingGrabObject.IsGrabbed}, " +
                $"Owner={pendingGrabObject.GrabbedByPlayer}, " +
                $"Role={pendingGrabObject.CurrentGrabRole}"
            );
            pendingGrabObject = null;
        }
    }

    private void UpdateGrabTarget()
    {
        if (grabbedObject == null)
            return;

        if (runner == null)
        {
            DebugMessage("UpdateGrabTarget skipped because runner is null.");
            return;
        }

        if (targetSendRate <= 0f)
        {
            DebugMessage("UpdateGrabTarget skipped because targetSendRate <= 0.");
            return;
        }

        if (Time.time < nextTargetSendTime)
            return;

        nextTargetSendTime = Time.time + 1f / targetSendRate;

        DebugMessage(
            $"UpdateGrabTarget running. " +
            $"Object={grabbedObject.name}, " +
            $"SendRate={targetSendRate}, " +
            $"NextSendTime={nextTargetSendTime}"
        );

        SendGrabTargetImmediately();
    }

    private void SendGrabTargetImmediately()
    {
        if (grabbedObject == null)
        {
            DebugMessage("SendGrabTargetImmediately skipped because grabbedObject is null.");
            return;
        }

        if (rayOrigin == null)
        {
            DebugMessage("SendGrabTargetImmediately skipped because rayOrigin is null.");
            return;
        }

        if (runner == null)
        {
            DebugMessage("SendGrabTargetImmediately skipped because runner is null.");
            return;
        }

        Ray ray = BuildRay();
        Vector3 targetPosition =
            ray.origin + ray.direction * grabbedDistance;

        Quaternion targetRotation = keepInitialRotation
            ? GetRayRotation() * grabbedRotationOffset
            : GetRayRotation();

        DebugMessage(
            $"Sending grab target. " +
            $"Object={grabbedObject.name}, " +
            $"Player={runner.LocalPlayer}, " +
            $"Role={grabRole}, " +
            $"TargetPosition={targetPosition}, " +
            $"TargetRotation={targetRotation.eulerAngles}"
        );

        grabbedObject.RPC_UpdateGrabTarget(
            runner.LocalPlayer,
            (int)grabRole,
            targetPosition,
            targetRotation
        );
    }

    private void EndGrab()
    {
        if (grabbedObject == null && pendingGrabObject != null)
        {
            DebugMessage(
                $"Cancel pending grab. Object={pendingGrabObject.name}, " +
                $"Player={runner?.LocalPlayer}, Role={grabRole}"
            );

            if (runner != null)
            {
                pendingGrabObject.RPC_RequestRelease(
                    runner.LocalPlayer,
                    (int)grabRole
                );
            }

            pendingGrabObject = null;
            return;
        }

        if (grabbedObject == null)
        {
            DebugMessage("Grab released but no object was currently grabbed.");
            return;
        }

        if (runner == null)
        {
            grabbedObject = null;
            return;
        }

        DebugMessage(
            $"Request release. Object={grabbedObject.name}, Player={runner.LocalPlayer}, Role={grabRole}"
        );

        grabbedObject.RPC_RequestRelease(
            runner.LocalPlayer,
            (int)grabRole
        );

        grabbedObject = null;
    }

    private void UpdateRayVisual()
    {
        if (lineRenderer == null || rayOrigin == null)
            return;

        lineRenderer.SetPosition(0, currentRay.origin);
        lineRenderer.SetPosition(1, currentRayVisualEnd);

        if (grabbedObject != null)
        {
            if (grabRayMaterial != null)
                lineRenderer.material = grabRayMaterial;
        }
        else if (pendingGrabObject != null || hoveredObject != null)
        {
            if (hoverRayMaterial != null)
                lineRenderer.material = hoverRayMaterial;
        }
        else
        {
            if (idleRayMaterial != null)
                lineRenderer.material = idleRayMaterial;
        }
    }

    private void OnDisable()
    {
        if (grabbedObject != null && runner != null)
        {
            grabbedObject.RPC_RequestRelease(
                runner.LocalPlayer,
                (int)grabRole
            );

            grabbedObject = null;
        }

        if (pendingGrabObject != null && runner != null)
        {
            pendingGrabObject.RPC_RequestRelease(
                runner.LocalPlayer,
                (int)grabRole
            );
        }

        pendingGrabObject = null;
        xrTriggerWasPressed = false;
    }

    private NetworkPhysicalGrabbable FindGrabbableUnderRay(out RaycastHit hitInfo)
    {
        hitInfo = default;

        if (rayOrigin == null)
            return null;

        if (TryFindGrabbableUnderRay(
                BuildRay(),
                out NetworkPhysicalGrabbable target,
                out RaycastHit hit,
                out _))
        {
            hitInfo = hit;
            return target;
        }

        return null;
    }

    private Ray BuildRay()
    {
        Vector3 origin = rayOrigin.TransformPoint(rayStartOffset);
        Quaternion rotation = GetRayRotation();
        return new Ray(origin, rotation * Vector3.forward);
    }

    private Quaternion GetRayRotation()
    {
        return rayOrigin.rotation * Quaternion.Euler(rayRotationOffset);
    }

    private bool TryFindGrabbableUnderRay(
        Ray ray,
        out NetworkPhysicalGrabbable target,
        out RaycastHit targetHit,
        out RaycastHit blockingHit)
    {
        target = null;
        targetHit = default;
        blockingHit = default;

        float maximumInteractionDistance = maxDistance;

        if (blockingLayers.value != 0 &&
            Physics.Raycast(
                ray,
                out blockingHit,
                maxDistance,
                blockingLayers,
                QueryTriggerInteraction.Ignore))
        {
            maximumInteractionDistance = blockingHit.distance;
        }

        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            maximumInteractionDistance,
            interactableLayers,
            QueryTriggerInteraction.Ignore
        );

        float closestDistance = float.PositiveInfinity;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null ||
                IsControllerCollider(hit.collider) ||
                hit.distance >= closestDistance)
            {
                continue;
            }

            NetworkPhysicalGrabbable candidate =
                hit.collider.GetComponentInParent<NetworkPhysicalGrabbable>();

            if (candidate == null)
                continue;

            target = candidate;
            targetHit = hit;
            closestDistance = hit.distance;
        }

        return target != null;
    }

    private bool IsControllerCollider(Collider candidate)
    {
        if (candidate == null || controllerRoot == null)
            return false;

        Transform candidateTransform = candidate.transform;
        return candidateTransform == controllerRoot ||
               candidateTransform.IsChildOf(controllerRoot);
    }

    private void DebugMessage(string message)
    {
        if (!debugLog)
            return;

        Debug.Log($"[ViveRayNetworkPhysicalGrabAdapter] {message}");
    }
}

#else

using UnityEngine;

public class ViveRayNetworkPhysicalGrabAdapter : MonoBehaviour
{
    private void Awake()
    {
        enabled = false;
    }
}

#endif
