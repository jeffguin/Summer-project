using UnityEngine;

public class TicTacToeSocketableObject : MonoBehaviour
{
    private TicTacToeSocket currentSocket;
    private Vector3 originalLocalScale;
    private bool hasOriginalScale;

    public bool IsSocketed => currentSocket != null;

    private void Awake()
    {
        CaptureOriginalScale();
    }

    private void CaptureOriginalScale()
    {
        if (hasOriginalScale)
            return;

        originalLocalScale = transform.localScale;
        hasOriginalScale = true;
    }

    public void SetSocket(TicTacToeSocket socket)
    {
        currentSocket = socket;
    }

    public void RestoreOriginalScale()
    {
        CaptureOriginalScale();
        transform.localScale = originalLocalScale;
    }

    public void RemoveFromSocket()
    {
        if (currentSocket != null)
        {
            currentSocket.RemoveObject();
            currentSocket = null;
        }
    }
}
