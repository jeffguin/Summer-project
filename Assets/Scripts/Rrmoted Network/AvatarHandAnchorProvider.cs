using System.Collections.Generic;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class AvatarHandAnchorProvider : MonoBehaviour
{
    private const string LeftAnchorName = "LeftHand_grab_anchor";
    private const string RightAnchorName = "RightHand_grab_anchor";

    private static readonly List<AvatarHandAnchorProvider> Instances = new();

    [SerializeField] private Transform leftAnchor;
    [SerializeField] private Transform rightAnchor;

    private NetworkObject networkRoot;
    private bool loggedMissingAnchors;

    public Transform LeftAnchor => leftAnchor;
    public Transform RightAnchor => rightAnchor;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        if (!Instances.Contains(this))
        {
            Instances.Add(this);
        }

        ResolveReferences();
    }

    private void OnDisable()
    {
        Instances.Remove(this);
    }

    public void ResolveReferences()
    {
        if (networkRoot == null)
        {
            networkRoot = GetComponentInParent<NetworkObject>();
        }

        if (leftAnchor == null || rightAnchor == null)
        {
            Transform[] descendants = GetComponentsInChildren<Transform>(true);

            foreach (Transform descendant in descendants)
            {
                if (leftAnchor == null && descendant.name == LeftAnchorName)
                {
                    leftAnchor = descendant;
                }
                else if (rightAnchor == null && descendant.name == RightAnchorName)
                {
                    rightAnchor = descendant;
                }

                if (leftAnchor != null && rightAnchor != null)
                {
                    break;
                }
            }
        }

        if ((leftAnchor == null || rightAnchor == null) && !loggedMissingAnchors)
        {
            Debug.LogError(
                "AvatarHandAnchorProvider: The spawned avatar must contain " +
                $"{LeftAnchorName} and {RightAnchorName} transforms.",
                this
            );
            loggedMissingAnchors = true;
        }
    }

    public bool TryGetAnchor(
        NetworkPhysicalGrabbable.GrabHand hand,
        out Transform anchor)
    {
        ResolveReferences();

        anchor = hand switch
        {
            NetworkPhysicalGrabbable.GrabHand.Left => leftAnchor,
            NetworkPhysicalGrabbable.GrabHand.Right => rightAnchor,
            _ => null
        };

        return anchor != null;
    }

    public static bool TryGetAnchor(
        PlayerRef player,
        NetworkPhysicalGrabbable.GrabHand hand,
        out Transform anchor)
    {
        anchor = null;

        for (int i = Instances.Count - 1; i >= 0; i--)
        {
            AvatarHandAnchorProvider provider = Instances[i];

            if (provider == null)
            {
                Instances.RemoveAt(i);
                continue;
            }

            provider.ResolveReferences();

            if (provider.networkRoot == null ||
                !provider.networkRoot.IsValid)
            {
                continue;
            }

            bool belongsToPlayer =
                provider.networkRoot.InputAuthority == player ||
                (provider.networkRoot.InputAuthority == PlayerRef.None &&
                 provider.networkRoot.StateAuthority == player);

            if (belongsToPlayer && provider.TryGetAnchor(hand, out anchor))
            {
                return true;
            }
        }

        return false;
    }
}
