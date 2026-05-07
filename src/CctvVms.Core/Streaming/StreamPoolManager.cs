using System.Collections.Concurrent;
using CctvVms.Core.Contracts;
using CctvVms.Core.Domain;
using LibVLCSharp.Shared;

namespace CctvVms.Core.Streaming;

public sealed class StreamPoolManager : IStreamPoolManager, IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly int _maxDecoders;
    private readonly GpuLoadBalancer _gpu;
    private readonly ConcurrentQueue<MediaPlayer> _available = new();
    private readonly ConcurrentDictionary<string, MediaPlayer> _active = new();
    // Tracks which active camera ids are using GPU decode
    private readonly ConcurrentDictionary<string, bool> _usingHardware = new();

    public StreamPoolManager(LibVLC libVlc, StreamEngineOptions options, GpuLoadBalancer gpu)
    {
        _libVlc = libVlc;
        _maxDecoders = options.MaxActiveDecoders;
        _gpu = gpu;

        // Pre-warm the pool — players start with hardware decode off;
        // EnableHardwareDecoding is toggled per-acquire based on GPU budget.
        for (var i = 0; i < _maxDecoders; i++)
        {
            _available.Enqueue(new MediaPlayer(_libVlc));
        }
    }

    public int ActiveDecoderCount => _active.Count;
    public int MaxDecoders => _maxDecoders;

    public MediaPlayer Acquire(string cameraId, StreamType streamType)
    {
        if (_active.TryGetValue(cameraId, out var existing))
        {
            existing.Mute = streamType == StreamType.Sub;
            return existing;
        }

        if (!_available.TryDequeue(out var player))
        {
            // Pool exhausted — evict the oldest idle active player
            var evictKey = _active
                .Where(kv => !kv.Value.IsPlaying)
                .Select(kv => kv.Key)
                .FirstOrDefault();

            if (evictKey is not null)
            {
                Release(evictKey);
            }

            if (!_available.TryDequeue(out var recycled))
            {
                throw new InvalidOperationException("Decoder capacity reached. Reduce active cameras or use sub-streams.");
            }

            player = recycled;
        }

        // Assign GPU or CPU decode based on current GPU budget
        var useHardware = _gpu.CanUseHardware();
        player.EnableHardwareDecoding = useHardware;
        if (useHardware) _gpu.Allocate();

        player.Mute = streamType == StreamType.Sub;
        _active[cameraId] = player;
        _usingHardware[cameraId] = useHardware;
        return player;
    }

    public void Release(string cameraId)
    {
        if (_active.TryRemove(cameraId, out var player))
        {
            // Free the GPU slot if this player was hardware-decoded
            if (_usingHardware.TryRemove(cameraId, out var wasHardware) && wasHardware)
            {
                _gpu.Release();
            }

            player.Stop();
            player.EnableHardwareDecoding = false; // reset before returning to pool
            _available.Enqueue(player);
        }
    }

    private MediaPlayer CreatePlayer()
    {
        return new MediaPlayer(_libVlc);
    }

    public void Dispose()
    {
        foreach (var (cameraId, player) in _active)
        {
            if (_usingHardware.TryGetValue(cameraId, out var hw) && hw) _gpu.Release();
            player.Stop();
            player.Dispose();
        }

        _active.Clear();
        _usingHardware.Clear();

        while (_available.TryDequeue(out var p))
        {
            p.Dispose();
        }
    }
}
