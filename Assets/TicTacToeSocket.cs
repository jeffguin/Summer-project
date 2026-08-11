using Fusion;
using UnityEngine;

public class TicTacToeSocket : MonoBehaviour
{
    [SerializeField]
    private Transform socketAnchor;

    private GameObject placedObject;

    private Rigidbody placedRb;

    private void OnTriggerEnter(Collider other)
    {
        TryPlaceObject(other);
    }

    private void OnTriggerStay(Collider other)
    {
        // A held piece can enter the trigger before it is released. Retrying
        // while it remains inside lets the State Authority snap it only after
        // the network grab has actually ended.
        TryPlaceObject(other);
    }

    private void TryPlaceObject(Collider other)
    {
        if (placedObject != null)
            return;

        GameObject candidate = ResolveSocketableRoot(other);
        if (candidate == null || !candidate.CompareTag("TicTacToe"))
            return;

        TicTacToeSocketableObject existingSocketable =
            candidate.GetComponent<TicTacToeSocketableObject>();

        if (existingSocketable != null && existingSocketable.IsSocketed)
            return;

        NetworkObject networkObject = candidate.GetComponent<NetworkObject>();

        if (networkObject != null)
        {
            // The socket is scene-local, so only the peer which owns the
            // NetworkObject state may decide that a network piece is placed.
            // NetworkTransform will replicate the snapped world pose.
            if (!networkObject.IsValid || !networkObject.HasStateAuthority)
            {
                return;
            }
        }

        NetworkPhysicalGrabbable networkGrabbable =
            candidate.GetComponent<NetworkPhysicalGrabbable>();

        if (networkGrabbable != null && networkGrabbable.IsGrabbed)
            return;

        PlaceObject(candidate);
    }

    private static GameObject ResolveSocketableRoot(Collider other)
    {
        TicTacToeSocketableObject socketable =
            other.GetComponentInParent<TicTacToeSocketableObject>();

        if (socketable != null)
        {
            return socketable.gameObject;
        }

        if (other.attachedRigidbody != null)
        {
            return other.attachedRigidbody.gameObject;
        }

        return other.gameObject;
    }

    private void PlaceObject(GameObject obj)
    {
        if (socketAnchor == null)
            return;

        TicTacToeSocketableObject socketable =
            obj.GetComponent<TicTacToeSocketableObject>();

        if (socketable == null)
        {
            socketable = obj.AddComponent<TicTacToeSocketableObject>();
        }

        placedObject = obj;
        placedRb = obj.GetComponent<Rigidbody>();

        // Never parent a NetworkObject to the Slot/Anchor hierarchy. Slot has
        // a very small non-uniform scale, which turns a later localScale=1
        // write from the grab transformer into a tiny world-space piece.
        obj.transform.SetParent(null, true);
        socketable.RestoreOriginalScale();
        obj.transform.SetPositionAndRotation(
            socketAnchor.position,
            socketAnchor.rotation
        );

        if (placedRb != null)
        {
            placedRb.isKinematic = true;
            placedRb.linearVelocity = Vector3.zero;
            placedRb.angularVelocity = Vector3.zero;
        }

        socketable.SetSocket(this);
    }

    public void RemoveObject()
    {
        if (placedObject == null)
            return;

        TicTacToeSocketableObject socketable =
            placedObject.GetComponent<TicTacToeSocketableObject>();

        if (socketable != null)
        {
            socketable.SetSocket(null);
        }

        // Backward-compatible cleanup in case an object was socket-parented
        // before this version of the component became active.
        if (placedObject.transform.parent == socketAnchor)
        {
            placedObject.transform.SetParent(null, true);
        }

        if (socketable != null)
        {
            socketable.RestoreOriginalScale();
        }

        if (placedRb != null)
        {
            placedRb.isKinematic = false;
        }

        placedObject = null;
        placedRb = null;
    }
}
