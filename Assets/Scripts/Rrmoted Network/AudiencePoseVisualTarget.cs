using UnityEngine;

[DisallowMultipleComponent]
public sealed class AudiencePoseVisualTarget : MonoBehaviour
{
    public enum TargetKind
    {
        Head,
        RightHand
    }

    [SerializeField] private TargetKind targetKind;

    public TargetKind Kind => targetKind;
}
