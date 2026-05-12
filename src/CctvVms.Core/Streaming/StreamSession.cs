using CctvVms.Core.Domain;
using LibVLCSharp.Shared;

namespace CctvVms.Core.Streaming;

public sealed class StreamSession : IDisposable
{
    public string CameraId { get; init; } = string.Empty;
    public StreamType StreamType { get; set; }
    public string SourceUrl { get; init; } = string.Empty;
    public MediaPlayer Player { get; init; } = null!;
    public DateTime StartedUtc { get; init; } = DateTime.UtcNow;
    public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;
    /// <summary>True once BeginPlayAsync has been called — gates health-monitor restarts.</summary>
    public bool HasBegunPlay { get; set; }
    /// <summary>Number of consecutive reconnect failures. Reset to 0 on successful play.</summary>
    public int FailureCount { get; set; }
    /// <summary>Do not attempt a reconnect before this time (exponential backoff).</summary>
    public DateTime NextRetryUtc { get; set; } = DateTime.MinValue;

    public void Dispose() => Player.Stop();
}
