using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class AudienceScreenClapDetector : MonoBehaviour
{
    [Header("Audience Screen Zone")]
    [SerializeField] private BoxCollider clapVolume;
    [SerializeField] private Transform rightHandSource;

    [Header("Diagnostics")]
    [SerializeField] private bool debugLog;

    private bool previousState;

    public bool IsHandInside { get; private set; }

    private void Awake()
    {
        if (clapVolume == null)
            clapVolume = GetComponent<BoxCollider>();
    }

    private void OnValidate()
    {
        if (clapVolume == null)
            clapVolume = GetComponent<BoxCollider>();

        if (clapVolume != null)
            clapVolume.isTrigger = true;
    }

    private void Update()
    {
        IsHandInside = rightHandSource != null &&
                       rightHandSource.gameObject.activeInHierarchy &&
                       IsPointInsideVolume(rightHandSource.position);

        if (debugLog && IsHandInside != previousState)
        {
            Debug.Log(
                "[AudienceScreenClapDetector] Right hand inside=" +
                IsHandInside + ".",
                this
            );
        }

        previousState = IsHandInside;
    }

    private bool IsPointInsideVolume(Vector3 worldPosition)
    {
        if (clapVolume == null)
            return false;

        Vector3 localPoint =
            clapVolume.transform.InverseTransformPoint(worldPosition) -
            clapVolume.center;
        Vector3 halfSize = clapVolume.size * 0.5f;

        return Mathf.Abs(localPoint.x) <= halfSize.x &&
               Mathf.Abs(localPoint.y) <= halfSize.y &&
               Mathf.Abs(localPoint.z) <= halfSize.z;
    }
}
