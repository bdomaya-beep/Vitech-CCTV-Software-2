using CctvVms.Core.Domain;

namespace CctvVms.Core.Streaming;

// Legacy session model — replaced by RtspVideoDecoder in the FFmpeg pipeline.
// Kept as a stub to avoid removing files that may be referenced elsewhere.
public sealed class StreamSession : IDisposable
{
    public string     CameraId          { get; init; } = string.Empty;
    public StreamType StreamType        { get; set; }
    public string     SourceUrl         { get; init; } = string.Empty;
    public DateTime   StartedUtc        { get; init; } = DateTime.UtcNow;
    public DateTime   LastHeartbeatUtc  { get; set; }  = DateTime.UtcNow;
    public bool       HasBegunPlay      { get; set; }
    public int        FailureCount      { get; set; }
    public DateTime   NextRetryUtc      { get; set; }  = DateTime.MinValue;

    public void Dispose() { }
}
