#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

using UnityEngine;
using Valve.VR;

public class ViveRayGrabInteractor : MonoBehaviour
{
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

    private RayGrabbableObject hoveredObject;
    private RayGrabbableObject grabbedObject;

    private float grabbedDistance;
    private Quaternion grabbedRotationOffset;

    private void Start()
    {
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 2;
            lineRenderer.startWidth = rayWidth;
            lineRenderer.endWidth = rayWidth;
            lineRenderer.enabled = true;
        }
    }

    private void Update()
    {
        UpdateRaycast();
        UpdateInput();
        UpdateGrabbedObject();
        UpdateRayVisual();
    }

    private void UpdateRaycast()
    {
        hoveredObject = null;

        if (rayOrigin == null)
            return;

        if (Physics.Raycast(
                rayOrigin.position,
                rayOrigin.forward,
                out RaycastHit hit,
                maxDistance,
                interactableLayers))
        {
            hoveredObject = hit.collider.GetComponentInParent<RayGrabbableObject>();
        }
    }

    private void UpdateInput()
    {
        if (grabAction == null)
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
            Debug.Log("ViveRayGrabInteractor: Grab pressed but no grabbable object hit.");
            return;
        }

        grabbedObject = hoveredObject;

        grabbedDistance = Vector3.Distance(
            rayOrigin.position,
            grabbedObject.transform.position
        );

        grabbedRotationOffset =
            Quaternion.Inverse(rayOrigin.rotation) * grabbedObject.transform.rotation;

        grabbedObject.OnGrabBegin();

        Debug.Log("ViveRayGrabInteractor: Grab begin - " + grabbedObject.name);
    }

    private void UpdateGrabbedObject()
    {
        if (grabbedObject == null || rayOrigin == null)
            return;

        Vector3 targetPosition =
            rayOrigin.position + rayOrigin.forward * grabbedDistance;

        Quaternion targetRotation =
            rayOrigin.rotation * grabbedRotationOffset;

        grabbedObject.OnGrabMove(targetPosition, targetRotation, true);
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
        Vector3 end = start + rayOrigin.forward * maxDistance;

        if (Physics.Raycast(
                rayOrigin.position,
                rayOrigin.forward,
                out RaycastHit hit,
                maxDistance,
                interactableLayers))
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
}

#else

using UnityEngine;

public class ViveRayGrabInteractor : MonoBehaviour
{
    private void Awake()
    {
        enabled = false;
    }
}

#endif