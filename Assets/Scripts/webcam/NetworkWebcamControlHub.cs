using Fusion;
using UnityEngine;

public class NetworkWebcamControlHub : NetworkBehaviour
{
    public void RequestStartAudienceVideo(int cameraIndex)
    {
        Debug.Log("NetworkWebcamControlHub: RequestStartAudienceVideo " + cameraIndex);

        // 下一步这里会改成 RPC，发送给 Audience Client
    }

    public void RequestStopAudienceVideo()
    {
        Debug.Log("NetworkWebcamControlHub: RequestStopAudienceVideo");

        // 下一步这里会改成 RPC，发送给 Audience Client
    }
}