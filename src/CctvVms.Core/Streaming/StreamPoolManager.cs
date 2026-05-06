using System.Collections.Concurrent;
using CctvVms.Core.Contracts;
using CctvVms.Core.Domain;
using LibVLCSharp.Shared;

namespace CctvVms.Core.Streaming;

public sealed class StreamPoolManager : IStreamPoolManager
{
    private readonly LibVLC _libVlc;
    private readonly int _maxDecoders;
    private readonly ConcurrentDictionary<string, MediaPlayer> _players = new();

    public StreamPoolManager(LibVLC libVlc, StreamEngineOptions options)
    {
        _libVlc = libVlc;
        _maxDecoders = options.MaxActiveDecoders;
    }

    public int ActiveDecoderCount => _players.Count;
    public int MaxDecoders => _maxDecoders;

    public MediaPlayer Acquire(string cameraId, StreamType streamType)
    {
        if (_players.TryGetValue(cameraId, out var existing))
        {
            return existing;
        }

        if (_players.Count >= _maxDecoders)
        {
            var oldest = _players
                .MinBy(x => x.Value.IsPlaying ? 1 : 0)
                .Key;
            Release(oldest);
        }

        var player = new MediaPlayer(_libVlc)
        {
            EnableHardwareDecoding = false,
            Mute = streamType == StreamType.Sub
        };

        if (!_players.TryAdd(cameraId, player))
        {
            player.Dispose();
            return _players[cameraId];
        }

        return player;
    }

    public void Release(string cameraId)
    {
        if (_players.TryRemove(cameraId, out var player))
        {
            player.Stop();
            player.Dispose();
        }
    }
}
