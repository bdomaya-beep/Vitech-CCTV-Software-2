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
    /// <summary>Set to true when BeginPlayAsync is called so the health monitor
    /// knows this session has actually started and is eligible for auto-restart.</summary>
    public bool HasBegunPlay { get; set; }

    public void Dispose()
    {
        Player.Stop();
    }
}