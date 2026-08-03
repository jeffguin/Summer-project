using System;

[Serializable]
public sealed class VideoStreamDescriptor
{
    public string streamId;
    public int cameraIndex;
    public string deviceName;
    public string trackId;
    public string mid;

    public VideoStreamDescriptor Clone()
    {
        return new VideoStreamDescriptor
        {
            streamId = streamId,
            cameraIndex = cameraIndex,
            deviceName = deviceName,
            trackId = trackId,
            mid = mid
        };
    }
}
