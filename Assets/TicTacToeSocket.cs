using UnityEngine;

public class TicTacToeSocket : MonoBehaviour
{
    [SerializeField]
    private Transform socketAnchor;

    private GameObject placedObject;

    private Rigidbody placedRb;


    private void OnTriggerEnter(Collider other)
    {
        // Socket already occupied
        if (placedObject != null)
            return;


        // Only accept TicTacToe objects
        if (other.CompareTag("TicTacToe"))
        {
            PlaceObject(other.gameObject);
        }
    }


    private void PlaceObject(GameObject obj)
    {
        placedObject = obj;

        placedRb = obj.GetComponent<Rigidbody>();


        // Snap object into socket position
        obj.transform.position = socketAnchor.position;
        obj.transform.rotation = socketAnchor.rotation;


        // Disable physics while placed
        if (placedRb != null)
        {
            placedRb.isKinematic = true;
            placedRb.linearVelocity = Vector3.zero;
            placedRb.angularVelocity = Vector3.zero;
        }


        // Parent to socket anchor
        obj.transform.SetParent(socketAnchor);


        // Tell the object which socket it belongs to
        TicTacToeSocketableObject socketable =
            obj.GetComponent<TicTacToeSocketableObject>();

        if (socketable == null)
        {
            socketable = obj.AddComponent<TicTacToeSocketableObject>();
        }

        socketable.SetSocket(this);
    }


    public void RemoveObject()
    {
        if (placedObject == null)
            return;


        // Clear socket reference on the object
        TicTacToeSocketableObject socketable =
            placedObject.GetComponent<TicTacToeSocketableObject>();

        if (socketable != null)
        {
            socketable.SetSocket(null);
        }


        // Detach from socket
        placedObject.transform.SetParent(null);


        // Restore physics
        if (placedRb != null)
        {
            placedRb.isKinematic = false;
        }


        // Clear socket data
        placedObject = null;
        placedRb = null;
    }
}