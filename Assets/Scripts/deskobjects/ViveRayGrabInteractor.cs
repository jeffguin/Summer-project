using UnityEngine;
using Valve.VR;

public class ViveRayGrabInteractor : MonoBehaviour
{
    [Header("Ray Source")]
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private float maxDistance = 5f;
    [SerializeField] private LayerMask interactableLayers = ~0;

    [Header("Input")]
    [SerializeField] private SteamVR_Action_Boolean grabAction;
    [SerializeField] private SteamVR_Input_Sources inputSource = SteamVR_Input_Sources.RightHand;

    [Header("Ray Visual")]
    [SerializeField] private LineRenderer lineRenderer;
    [SerializeField] private Material idleRayMaterial;
    [SerializeField] private Material hoverRayMaterial;
    [SerializeField] private Material grabRayMaterial;
    [SerializeField] private float rayWidth = 0.01f;

    [Header("Grab Behaviour")]
    [SerializeField] private bool keepInitialRotation = true;
    [SerializeField] private bool rotateWithController = false;
    [SerializeField] private float defaultGrabDistance = 1.5f;

    [Header("Debug")]
    [SerializeField] private bool drawDebugRay = true;

    private RayGrabbableObject hoveredObject;
    private RayGrabbableObject grabbedObject;

    private float grabbedDistance;
    private Vector3 localGrabOffset;
    private Quaternion grabbedObjectInitialRotation;
    private Quaternion controllerInitialRotation;
    private bool hasHit;
    private RaycastHit currentHit;

    private enum RayState
    {
        Idle,
        Hover,
        Grab
    }

    private void Reset()
    {
        rayOrigin = transform;
    }

    private void Awake()
    {
        if (rayOrigin == null)
            rayOrigin = transform;

        SetupLineRenderer();
    }

    private void Update()
    {
        UpdateRaycast();
        UpdateInput();
        UpdateGrabbedObject();
        UpdateRayVisual();
    }

    private void SetupLineRenderer()
    {
        if (lineRenderer == null)
            return;

        lineRenderer.positionCount = 2;
        lineRenderer.useWorldSpace = true;
        lineRenderer.startWidth = rayWidth;
        lineRenderer.endWidth = rayWidth;
    }

    private void UpdateRaycast()
    {
        if (rayOrigin == null)
            return;

        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);

        hasHit = Physics.Raycast(
            ray,
            out currentHit,
            maxDistance,
            interactableLayers,
            QueryTriggerInteraction.Collide
        );

        hoveredObject = null;

        if (hasHit)
        {
            hoveredObject = currentHit.collider.GetComponentInParent<RayGrabbableObject>();

            if (hoveredObject != null && !hoveredObject.AllowGrab)
            {
                hoveredObject = null;
            }
        }

        if (drawDebugRay)
        {
            Color debugColor = hoveredObject != null ? Color.green : Color.gray;
            Debug.DrawRay(ray.origin, ray.direction * maxDistance, debugColor);
        }
    }

    private void UpdateInput()
    {
        if (grabAction == null)
        {
            Debug.LogWarning("ViveRayGrabInteractor: Grab Action is not assigned.");
            return;
        }

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
        if (grabbedObject != null)
            return;

        if (hoveredObject == null)
        {
            Debug.Log("ViveRayGrabInteractor: Grab pressed but no grabbable object hit.");
            return;
        }

        grabbedObject = hoveredObject;

        if (hasHit)
        {
            grabbedDistance = Vector3.Distance(rayOrigin.position, currentHit.point);

            // 保留手柄命中点到物体中心的偏移，避免抓取瞬间物体跳动
            localGrabOffset = grabbedObject.transform.InverseTransformPoint(currentHit.point);
        }
        else
        {
            grabbedDistance = defaultGrabDistance;
            localGrabOffset = Vector3.zero;
        }

        grabbedObjectInitialRotation = grabbedObject.transform.rotation;
        controllerInitialRotation = rayOrigin.rotation;

        grabbedObject.OnGrabBegin();

        Debug.Log("ViveRayGrabInteractor: Grab begin - " + grabbedObject.name);
    }

    private void UpdateGrabbedObject()
    {
        if (grabbedObject == null)
            return;

        Vector3 targetHitPoint = rayOrigin.position + rayOrigin.forward * grabbedDistance;

        // 把最初抓住的局部点对齐到当前射线目标点
        Vector3 worldOffset = grabbedObject.transform.TransformVector(localGrabOffset);
        Vector3 targetObjectPosition = targetHitPoint - worldOffset;

        Quaternion targetRotation = grabbedObjectInitialRotation;

        if (rotateWithController)
        {
            Quaternion controllerDelta = rayOrigin.rotation * Quaternion.Inverse(controllerInitialRotation);
            targetRotation = controllerDelta * grabbedObjectInitialRotation;
        }
        else if (!keepInitialRotation)
        {
            targetRotation = rayOrigin.rotation;
        }

        grabbedObject.OnGrabMove(
            targetObjectPosition,
            targetRotation,
            rotateWithController || !keepInitialRotation
        );
    }

    private void EndGrab()
    {
        if (grabbedObject == null)
            return;

        grabbedObject.OnGrabEnd();

        Debug.Log("ViveRayGrabInteractor: Grab end - " + grabbedObject.name);

        grabbedObject = null;
    }

    private void UpdateRayVisual()
    {
        if (lineRenderer == null || rayOrigin == null)
            return;

        Vector3 start = rayOrigin.position;
        Vector3 end;

        if (grabbedObject != null)
        {
            end = rayOrigin.position + rayOrigin.forward * grabbedDistance;
            ApplyRayState(RayState.Grab);
        }
        else if (hoveredObject != null && hasHit)
        {
            end = currentHit.point;
            ApplyRayState(RayState.Hover);
        }
        else
        {
            end = rayOrigin.position + rayOrigin.forward * maxDistance;
            ApplyRayState(RayState.Idle);
        }

        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, end);
    }

    private void ApplyRayState(RayState state)
    {
        if (lineRenderer == null)
            return;

        switch (state)
        {
            case RayState.Idle:
                if (idleRayMaterial != null)
                    lineRenderer.material = idleRayMaterial;
                break;

            case RayState.Hover:
                if (hoverRayMaterial != null)
                    lineRenderer.material = hoverRayMaterial;
                break;

            case RayState.Grab:
                if (grabRayMaterial != null)
                    lineRenderer.material = grabRayMaterial;
                break;
        }
    }
}