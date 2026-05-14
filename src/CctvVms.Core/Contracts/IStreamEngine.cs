using CctvVms.Core.Domain;
using CctvVms.Core.Streaming;

namespace CctvVms.Core.Contracts;

public sealed class ActiveStreamInfo
{
    public string      CameraId    { get; init; } = string.Empty;
    public StreamType  StreamType  { get; init; }
    public DateTime    StartedUtc  { get; init; }
    public bool        IsHealthy   { get; init; }
    public IVideoSource? VideoSource { get; init; }
}

public interface IStreamEngine
{
    Task<ActiveStreamInfo> StartStreamAsync(CameraEntity camera, StreamType streamType, CancellationToken cancellationToken = default);
    Task BeginPlayAsync(string cameraId, CancellationToken cancellationToken = default);
    Task StopStreamAsync(string cameraId, CancellationToken cancellationToken = default);
    Task<ActiveStreamInfo> SwitchStreamAsync(CameraEntity camera, StreamType newType, CancellationToken cancellationToken = default);
    IReadOnlyList<ActiveStreamInfo> GetActiveStreams();
}
