using CctvVms.Core.Domain;
using LibVLCSharp.Shared;

namespace CctvVms.Core.Contracts;

public interface IStreamPoolManager
{
    int ActiveDecoderCount { get; }
    int MaxDecoders { get; }

    MediaPlayer Acquire(string cameraId, StreamType streamType);
    void Release(string cameraId);
}
