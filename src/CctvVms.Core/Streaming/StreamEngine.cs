using System.Collections.Concurrent;
using CctvVms.Core.Contracts;
using CctvVms.Core.Domain;
using LibVLCSharp.Shared;

namespace CctvVms.Core.Streaming;

public sealed class StreamEngine : IStreamEngine, IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly IStreamPoolManager _pool;
    private readonly StreamEngineOptions _options;
    private readonly ConcurrentDictionary<string, StreamSession> _sessions = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _cameraGuards = new();
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _monitorTask;
    private int _disposed;

    public StreamEngine(LibVLC libVlc, IStreamPoolManager pool, StreamEngineOptions options)
    {
        _libVlc = libVlc;
        _pool = pool;
        _options = options;
        _timer = new PeriodicTimer(options.HealthCheckInterval);
        _monitorTask = MonitorHealthAsync(_cts.Token);
    }

    public async Task<ActiveStreamInfo> StartStreamAsync(CameraEntity camera, StreamType streamType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(camera.Id))
        {
            throw new InvalidOperationException("Camera ID is required.");
        }

        var cameraGuard = _cameraGuards.GetOrAdd(camera.Id, static _ => new SemaphoreSlim(1, 1));
        await cameraGuard.WaitAsync(cancellationToken);
        try
        {
            if (streamType == StreamType.Main)
            {
                var activeMain = _sessions.Values.Count(static s => s.StreamType == StreamType.Main);
                if (activeMain >= _options.MaxMainStreams)
                {
                    streamType = StreamType.Sub;
                }
            }

            var url = ResolveUrl(camera, streamType);
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException("No stream URL configured for selected stream type.");
            }

            if (_sessions.TryGetValue(camera.Id, out var existing))
            {
                existing.LastHeartbeatUtc = DateTime.UtcNow;
                return ToInfo(existing, true);
            }

            var player = _pool.Acquire(camera.Id, streamType);

            var session = new StreamSession
            {
                CameraId = camera.Id,
                StreamType = streamType,
                SourceUrl = url,
                Player = player,
                LastHeartbeatUtc = DateTime.UtcNow
            };

            _sessions[camera.Id] = session;
            // NOTE: Do NOT call player.Play() here.
            // The caller must set tile.MediaPlayer first so VideoView binds its HWND,
            // then call BeginPlayAsync(). Calling Play() before VideoView is ready
            // causes VLC to open a new native window instead of rendering in-app.
            return ToInfo(session, false);
        }
        finally
        {
            cameraGuard.Release();
        }
    }

    public Task BeginPlayAsync(string cameraId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(cameraId, out var session))
        {
            var media = new Media(_libVlc, session.SourceUrl, FromType.FromLocation);
            session.Player.Play(media);
            // media ref will be cleaned up by VLC internally after it starts loading
            session.LastHeartbeatUtc = DateTime.UtcNow;
        }

        return Task.CompletedTask;
    }

    public async Task StopStreamAsync(string cameraId, CancellationToken cancellationToken = default)
    {
        var cameraGuard = _cameraGuards.GetOrAdd(cameraId, static _ => new SemaphoreSlim(1, 1));
        await cameraGuard.WaitAsync(cancellationToken);
        try
        {
            if (_sessions.TryRemove(cameraId, out var session))
            {
                session.Dispose();
                _pool.Release(cameraId);
            }
        }
        finally
        {
            cameraGuard.Release();
        }
    }

    public async Task<ActiveStreamInfo> SwitchStreamAsync(CameraEntity camera, StreamType newType, CancellationToken cancellationToken = default)
    {
        await StopStreamAsync(camera.Id, cancellationToken);
        return await StartStreamAsync(camera, newType, cancellationToken);
    }

    public IReadOnlyList<ActiveStreamInfo> GetActiveStreams()
    {
        return _sessions.Values
            .Select(session => ToInfo(session, session.Player.IsPlaying))
            .ToList();
    }

    private static string ResolveUrl(CameraEntity camera, StreamType streamType)
    {
        return streamType switch
        {
            StreamType.Main => camera.RtspMainUrl,
            StreamType.Sub => string.IsNullOrWhiteSpace(camera.RtspSubUrl) ? camera.RtspMainUrl : camera.RtspSubUrl,
            StreamType.Playback => camera.RtspMainUrl,
            _ => camera.RtspSubUrl
        };
    }

    private static ActiveStreamInfo ToInfo(StreamSession session, bool isHealthy)
    {
        return new ActiveStreamInfo
        {
            CameraId = session.CameraId,
            StreamType = session.StreamType,
            StartedUtc = session.StartedUtc,
            IsHealthy = isHealthy,
            MediaPlayer = session.Player
        };
    }

    private async Task MonitorHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(cancellationToken))
            {
                var now = DateTime.UtcNow;
                var stale = _sessions.Values
                    .Where(s => now - s.LastHeartbeatUtc > _options.StaleSessionThreshold)
                    .ToList();

                foreach (var session in stale)
                {
                    _sessions.TryRemove(session.CameraId, out _);
                    _pool.Release(session.CameraId);
                }

                foreach (var session in _sessions.Values)
                {
                    // Only restart players that have genuinely failed.
                    // Buffering and Opening are transient states — restarting them
                    // creates an infinite restart loop while the stream is loading.
                    var state = session.Player.State;
                    if (state == VLCState.Error ||
                        state == VLCState.Ended ||
                        state == VLCState.Stopped)
                    {
                        using var media = new Media(_libVlc, session.SourceUrl, FromType.FromLocation);
                        session.Player.Play(media);
                    }

                    session.LastHeartbeatUtc = now;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
        catch (ObjectDisposedException)
        {
            // Expected if timer/cts disposed while loop is unwinding.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed by a prior shutdown path.
        }

        _timer.Dispose();
        foreach (var cameraId in _sessions.Keys)
        {
            _pool.Release(cameraId);
        }

        _cts.Dispose();
    }
}
