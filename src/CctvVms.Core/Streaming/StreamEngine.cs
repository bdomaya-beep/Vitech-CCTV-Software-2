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
    private readonly SemaphoreSlim _reconnectGate = new SemaphoreSlim(3, 3);

    private static readonly TimeSpan[] BackoffTable = new TimeSpan[]
    {
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(32), TimeSpan.FromSeconds(60),
    };

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
            throw new InvalidOperationException("Camera ID is required.");

        var cameraGuard = _cameraGuards.GetOrAdd(camera.Id, static _ => new SemaphoreSlim(1, 1));
        await cameraGuard.WaitAsync(cancellationToken);
        try
        {
            if (streamType == StreamType.Main)
            {
                var activeMain = _sessions.Values.Count(static s => s.StreamType == StreamType.Main);
                if (activeMain >= _options.MaxMainStreams)
                    streamType = StreamType.Sub;
            }

            var url = ResolveUrl(camera, streamType);
            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("No stream URL configured for selected stream type.");

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
            using var media = new Media(_libVlc, session.SourceUrl, FromType.FromLocation);
            session.Player.Play(media);
            session.HasBegunPlay = true;
            session.FailureCount = 0;
            session.NextRetryUtc = DateTime.MinValue;
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
            if (_sessions.TryRemove(cameraId, out _))
            {
                await Task.Run(() => _pool.Release(cameraId), cancellationToken);
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
            StreamType.Main     => camera.RtspMainUrl,
            StreamType.Sub      => string.IsNullOrWhiteSpace(camera.RtspSubUrl) ? camera.RtspMainUrl : camera.RtspSubUrl,
            StreamType.Playback => camera.RtspMainUrl,
            _                   => camera.RtspSubUrl
        };
    }

    private static ActiveStreamInfo ToInfo(StreamSession session, bool isHealthy)
    {
        return new ActiveStreamInfo
        {
            CameraId    = session.CameraId,
            StreamType  = session.StreamType,
            StartedUtc  = session.StartedUtc,
            IsHealthy   = isHealthy,
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
                    var state = session.Player.State;

                    // Reset backoff when stream is healthy.
                    if (state == VLCState.Playing)
                    {
                        session.FailureCount = 0;
                        session.NextRetryUtc = DateTime.MinValue;
                    }

                    // Only restart streams that have genuinely failed (Error/Ended).
                    // Never restart Stopped (intentional) or Buffering/Opening (transient).
                    // Use a gate to prevent hammering the NVR with simultaneous reconnects.
                    if (session.HasBegunPlay &&
                        (state == VLCState.Error || state == VLCState.Ended) &&
                        now >= session.NextRetryUtc &&
                        _reconnectGate.Wait(0))
                    {
                        try
                        {
                            var delay = BackoffTable[Math.Min(session.FailureCount, BackoffTable.Length - 1)];
                            session.FailureCount++;
                            session.NextRetryUtc = now + delay;
                            using var media = new Media(_libVlc, session.SourceUrl, FromType.FromLocation);
                            session.Player.Play(media);
                        }
                        finally { _reconnectGate.Release(); }
                    }

                    session.LastHeartbeatUtc = now;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        try { _cts.Cancel(); } catch (ObjectDisposedException) { }

        _timer.Dispose();
        foreach (var cameraId in _sessions.Keys)
            _pool.Release(cameraId);

        _cts.Dispose();
    }
}