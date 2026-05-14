namespace CctvVms.Core.Contracts;

// Replaced by per-stream RtspVideoDecoder in the new FFmpeg pipeline.
public interface IStreamPoolManager
{
    int ActiveDecoderCount { get; }
    int MaxDecoders        { get; }
}
