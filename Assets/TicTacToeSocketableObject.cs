using UnityEngine;

public class TicTacToeSocketableObject : MonoBehaviour
{
    private TicTacToeSocket currentSocket;


    public void SetSocket(TicTacToeSocket socket)
    {
        currentSocket = socket;
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