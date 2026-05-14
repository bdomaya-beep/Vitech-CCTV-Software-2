using System.Collections.Concurrent;
using CctvVms.Core.Contracts;
using CctvVms.Core.Domain;

namespace CctvVms.Core.Streaming;

public sealed class StreamEngine : IStreamEngine, IDisposable
{
    private readonly StreamEngineOptions _options;
    private readonly ConcurrentDictionary<string, RtspVideoDecoder> _decoders = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim>    _guards   = new();
    private int _disposed;

    public StreamEngine(StreamEngineOptions options) => _options = options;

    public async Task<ActiveStreamInfo> StartStreamAsync(CameraEntity camera, StreamType streamType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(camera.Id))
            throw new InvalidOperationException("Camera ID is required.");

        var guard = _guards.GetOrAdd(camera.Id, static _ => new SemaphoreSlim(1, 1));
        await guard.WaitAsync(ct);
        try
        {
            if (_decoders.TryGetValue(camera.Id, out var existing))
            {
                // Reuse only if same stream type; otherwise replace with correct one
                if (existing.StreamType == streamType)
                    return ToInfo(existing, true);
                // Wrong type — dispose old and fall through to create new decoder
                if (_decoders.TryRemove(camera.Id, out var stale))
                    stale.Dispose();
            }

            var url = ResolveUrl(camera, streamType);
            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("No stream URL configured.");

            var dec = new RtspVideoDecoder(url, streamType, _options.RtspTransport) { CameraId = camera.Id };
            _decoders[camera.Id] = dec;
            return ToInfo(dec, false);
        }
        finally { guard.Release(); }
    }

    public Task BeginPlayAsync(string cameraId, CancellationToken ct = default)
    {
        if (_decoders.TryGetValue(cameraId, out var dec) && !dec.IsRunning)
            dec.Start();
        return Task.CompletedTask;
    }

    public async Task StopStreamAsync(string cameraId, CancellationToken ct = default)
    {
        var guard = _guards.GetOrAdd(cameraId, static _ => new SemaphoreSlim(1, 1));
        await guard.WaitAsync(ct);
        try
        {
            if (_decoders.TryRemove(cameraId, out var dec))
                dec.Dispose();
        }
        finally { guard.Release(); }
    }

    public async Task<ActiveStreamInfo> SwitchStreamAsync(CameraEntity camera, StreamType newType, CancellationToken ct = default)
    {
        await StopStreamAsync(camera.Id, ct);
        return await StartStreamAsync(camera, newType, ct);
    }

    public IReadOnlyList<ActiveStreamInfo> GetActiveStreams() =>
        _decoders.Values.Select(d => ToInfo(d, d.IsRunning)).ToList();

    private static string ResolveUrl(CameraEntity camera, StreamType streamType) => streamType switch
    {
        StreamType.Main     => camera.RtspMainUrl,
        StreamType.Sub      => string.IsNullOrWhiteSpace(camera.RtspSubUrl) ? camera.RtspMainUrl : camera.RtspSubUrl,
        StreamType.Playback => camera.RtspMainUrl,
        _                   => camera.RtspSubUrl
    };

    private static ActiveStreamInfo ToInfo(RtspVideoDecoder dec, bool healthy) => new()
    {
        CameraId    = dec.CameraId,
        StreamType  = dec.StreamType,
        StartedUtc  = dec.StartedUtc,
        IsHealthy   = healthy,
        VideoSource = dec,
    };

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        foreach (var dec in _decoders.Values) dec.Dispose();
    }
}


