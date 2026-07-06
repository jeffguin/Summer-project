#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

using Fusion;
using UnityEngine;
using Valve.VR;

public class ViveRayNetworkPhysicalGrabAdapter : MonoBehaviour
{
    [Header("Role")]
    [SerializeField] private NetworkPhysicalGrabbable.GrabRole grabRole =
        NetworkPhysicalGrabbable.GrabRole.Audience;

    [Header("Ray Source")]
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private float maxDistance = 5f;
    [SerializeField] private LayerMask interactableLayers = ~0;

    [Header("SteamVR Input")]
    [SerializeField] private SteamVR_Action_Boolean grabAction;
    [SerializeField] private SteamVR_Input_Sources inputSource = SteamVR_Input_Sources.RightHand;

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

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;
    [SerializeField] private bool drawDebugRay = false;

    private NetworkRunner runner;

    private NetworkPhysicalGrabbable hoveredObject;
    private NetworkPhysicalGrabbable grabbedObject;

    private float grabbedDistance;
    private Quaternion grabbedRotationOffset;
    private float nextTargetSendTime;

    private void Start()
    {
        runner = FindObjectOfType<NetworkRunner>();

        if (rayOrigin == null)
        {
            Debug.LogError("ViveRayNetworkPhysicalGrabAdapter: RayOrigin is missing.");
        }

        if (grabAction == null)
        {
            Debug.LogError("ViveRayNetworkPhysicalGrabAdapter: GrabAction is missing.");
        }

        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 2;
            lineRenderer.startWidth = rayWidth;
            lineRenderer.endWidth = rayWidth;
            lineRenderer.enabled = true;
        }

        DebugMessage(
            $"Started. RunnerFound={runner != null}, Role={grabRole}, InputSource={inputSource}"
        );
    }

    private void Update()
    {
        if (runner == null)
        {
            runner = FindObjectOfType<NetworkRunner>();
        }

        UpdateHover();
        UpdateInput();
        UpdateGrabTarget();
        UpdateRayVisual();
    }

    private void UpdateHover()
    {
        hoveredObject = null;

        if (rayOrigin == null)
            return;

        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);

        if (drawDebugRay)
        {
            Debug.DrawRay(ray.origin, ray.direction * maxDistance, Color.cyan);
        }

        if (Physics.Raycast(
                ray,
                out RaycastHit hit,
                maxDistance,
                interactableLayers,
                QueryTriggerInteraction.Ignore))
        {
            hoveredObject = hit.collider.GetComponentInParent<NetworkPhysicalGrabbable>();

            if (hoveredObject != null)
            {
                DebugMessage($"Hovering: {hoveredObject.name}, HitPoint={hit.point}");
            }
        }
    }

    private void UpdateInput()
    {
        if (grabAction == null)
            return;

        if (runner == null)
            return;

        if (grabAction.GetStateDown(inputSource))
        {
            TryBeginGrab();
        }

        if (grabAction.GetStateUp(inputSource))
        {
            EndGrab();
        }
    }

    private void TryBeginGrab()
    {
        if (hoveredObject == null)
        {
            DebugMessage("Grab pressed but no NetworkPhysicalGrabbable was hit.");
            return;
        }

        grabbedObject = hoveredObject;

        grabbedDistance = Vector3.Distance(
            rayOrigin.position,
            grabbedObject.transform.position
        );

        if (grabbedDistance <= 0.05f)
        {
            grabbedDistance = defaultGrabDistance;
        }

        if (keepInitialRotation)
        {
            grabbedRotationOffset =
                Quaternion.Inverse(rayOrigin.rotation) * grabbedObject.transform.rotation;
        }
        else
        {
            grabbedRotationOffset = Quaternion.identity;
        }

        DebugMessage(
            $"Request grab. Object={grabbedObject.name}, " +
            $"Player={runner.LocalPlayer}, Role={grabRole}, Distance={grabbedDistance}"
        );

        grabbedObject.RPC_RequestGrab(
            runner.LocalPlayer,
            (int)grabRole
        );

        SendGrabTargetImmediately();
    }

    private void UpdateGrabTarget()
    {
        if (grabbedObject == null)
            return;

        if (runner == null)
            return;

        if (Time.time < nextTargetSendTime)
            return;

        nextTargetSendTime = Time.time + 1f / targetSendRate;

        SendGrabTargetImmediately();
    }

    private void SendGrabTargetImmediately()
    {
        if (grabbedObject == null || rayOrigin == null || runner == null)
            return;

        Vector3 targetPosition =
            rayOrigin.position + rayOrigin.forward * grabbedDistance;

        Quaternion targetRotation = keepInitialRotation
            ? rayOrigin.rotation * grabbedRotationOffset
            : rayOrigin.rotation;

        grabbedObject.RPC_UpdateGrabTarget(
            runner.LocalPlayer,
            (int)grabRole,
            targetPosition,
            targetRotation
        );
    }

    private void EndGrab()
    {
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

        Vector3 start = rayOrigin.position;
        Vector3 end = start + rayOrigin.forward * maxDistance;

        if (Physics.Raycast(
                rayOrigin.position,
                rayOrigin.forward,
                out RaycastHit hit,
                maxDistance,
                interactableLayers,
                QueryTriggerInteraction.Ignore))
        {
            end = hit.point;
        }

        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, end);

        if (grabbedObject != null)
        {
            if (grabRayMaterial != null)
                lineRenderer.material = grabRayMaterial;
        }
        else if (hoveredObject != null)
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