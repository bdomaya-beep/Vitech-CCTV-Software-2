namespace CctvVms.Core.Streaming;

public enum VideoPixelFormat { Yuv420P, Nv12 }

/// <summary>Decoded video frame with packed (no-padding) plane data.</summary>
public sealed class VideoFrame
{
    public int Width   { get; init; }
    public int Height  { get; init; }
    public VideoPixelFormat Format { get; init; }
    /// <summary>YUV420P: [Y,U,V]; NV12: [Y, UV-interleaved].</summary>
    public byte[][] Planes  { get; init; } = Array.Empty<byte[]>();
    public int[]    Strides { get; init; } = Array.Empty<int>();
    public bool IsValid => Width > 0 && Height > 0 && Planes.Length > 0;
}


