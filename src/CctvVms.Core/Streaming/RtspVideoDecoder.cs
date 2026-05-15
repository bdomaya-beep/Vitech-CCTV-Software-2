using System.Threading.Channels;
using CctvVms.Core.Domain;
using FFmpeg.AutoGen;

namespace CctvVms.Core.Streaming;

public sealed class RtspVideoDecoder : IVideoSource, IDisposable
{
    private readonly string _url;
    private readonly string _transport;
    private readonly List<Channel<VideoFrame>> _subscribers = new();
    private readonly object _subLock = new();
    private CancellationTokenSource _cts = new();
    private Task? _decodeTask;
    private int _failureCount;
    private int _disposed;

    private static readonly TimeSpan[] Backoff =
    {
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
    };

    public string CameraId { get; init; } = string.Empty;
    public StreamType StreamType { get; }
    public DateTime StartedUtc { get; } = DateTime.UtcNow;
    public bool IsRunning { get; private set; }
    public int FailureCount => _failureCount;

    public RtspVideoDecoder(string url, StreamType streamType, string rtspTransport = "tcp")
    {
        _url = url;
        _transport = rtspTransport;
        StreamType = streamType;
    }

    private static bool IsRetryableReadError(int errorCode) =>
        errorCode == -11 ||
        errorCode == -110;

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        _cts = new CancellationTokenSource();

        // One decoder worker thread per camera. Avoid thread-pool starvation across many streams.
        _decodeTask = Task.Factory
            .StartNew(() => DecodeLoopAsync(_cts.Token), _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default)
            .Unwrap();
    }

    public void Stop()
    {
        IsRunning = false;
        _cts.Cancel();
    }

    public ChannelReader<VideoFrame> Subscribe()
    {
        bool isPlayback = StreamType == StreamType.Playback;
        var opts = new BoundedChannelOptions(isPlayback ? 30 : 6)
        {
            FullMode = isPlayback ? BoundedChannelFullMode.Wait : BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        };
        var ch = Channel.CreateBounded<VideoFrame>(opts);
        lock (_subLock) _subscribers.Add(ch);
        return ch.Reader;
    }

    public void Unsubscribe(ChannelReader<VideoFrame> reader)
    {
        lock (_subLock) _subscribers.RemoveAll(ch => ReferenceEquals(ch.Reader, reader));
    }

    private async Task DecodeLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                DecodeStream(ct);
                _failureCount = 0;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SoftRetryException)
            {
                try { await Task.Delay(TimeSpan.FromMilliseconds(1000), ct); }
                catch (OperationCanceledException) { break; }
            }
            catch
            {
                var delay = Backoff[Math.Min(_failureCount, Backoff.Length - 1)];
                _failureCount = Math.Min(_failureCount + 1, Backoff.Length - 1);
                try { await Task.Delay(delay, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        lock (_subLock)
            foreach (var ch in _subscribers)
                ch.Writer.TryComplete();
    }

    private unsafe void DecodeStream(CancellationToken ct)
    {
        AVFormatContext* fmt = null;
        try
        {
            AVDictionary* opts = null;
            ffmpeg.av_dict_set(&opts, "rtsp_transport", _transport, 0);
            ffmpeg.av_dict_set(&opts, "stimeout", "5000000", 0);  // 5000 ms
            ffmpeg.av_dict_set(&opts, "rw_timeout", "5000000", 0); // 5000 ms
            ffmpeg.av_dict_set(&opts, "tcp_nodelay", "1", 0);
            ffmpeg.av_dict_set(&opts, "analyzeduration", "5000000", 0);
            ffmpeg.av_dict_set(&opts, "probesize", "5000000", 0);
            ffmpeg.av_dict_set(&opts, "fflags", "discardcorrupt", 0);
            if (StreamType != StreamType.Playback)
            {
                ffmpeg.av_dict_set(&opts, "flags", "low_delay", 0);
                ffmpeg.av_dict_set(&opts, "max_delay", "1000000", 0);
            }

            int ret = ffmpeg.avformat_open_input(&fmt, _url, null, &opts);
            ffmpeg.av_dict_free(&opts);
            if (ret < 0) throw new InvalidOperationException($"avformat_open_input failed ({ret})");

            ct.ThrowIfCancellationRequested();
            ffmpeg.avformat_find_stream_info(fmt, null);
            ct.ThrowIfCancellationRequested();

            int videoIdx = -1;
            for (int i = 0; i < (int)fmt->nb_streams; i++)
                if (fmt->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    videoIdx = i;
                    break;
                }
            if (videoIdx < 0) throw new SoftRetryException("No video stream found");

            AVCodecParameters* par = fmt->streams[videoIdx]->codecpar;
            AVCodec* codec = ffmpeg.avcodec_find_decoder(par->codec_id);
            if (codec == null) throw new InvalidOperationException($"No decoder for codec {par->codec_id}");

            AVCodecContext* ctx = ffmpeg.avcodec_alloc_context3(codec);
            ffmpeg.avcodec_parameters_to_context(ctx, par);
            ctx->thread_count = StreamType == StreamType.Playback ? Math.Max(2, Environment.ProcessorCount / 2) : 1;
            ctx->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;

            if (ffmpeg.avcodec_open2(ctx, codec, null) < 0)
            {
                ffmpeg.avcodec_free_context(&ctx);
                throw new InvalidOperationException("avcodec_open2 failed");
            }

            try
            {
                AVPacket* pkt = ffmpeg.av_packet_alloc();
                AVFrame* frame = ffmpeg.av_frame_alloc();
                bool seenKey = false;
                int consecutiveErrors = 0;
                int consecutiveReadTimeouts = 0;

                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        int r = ffmpeg.av_read_frame(fmt, pkt);

                        if (r == ffmpeg.AVERROR_EOF)
                            break;

                        if (r < 0)
                        {
                            if (IsRetryableReadError(r) && ++consecutiveReadTimeouts < 12)
                                continue;

                            throw new InvalidOperationException($"av_read_frame error {r}");
                        }

                        consecutiveReadTimeouts = 0;

                        if (pkt->stream_index != videoIdx)
                        {
                            ffmpeg.av_packet_unref(pkt);
                            continue;
                        }

                        bool isKey = (pkt->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
                        if (!seenKey && !isKey)
                        {
                            ffmpeg.av_packet_unref(pkt);
                            continue;
                        }
                        seenKey = true;

                        int sr = ffmpeg.avcodec_send_packet(ctx, pkt);
                        if (sr >= 0)
                        {
                            consecutiveErrors = 0;
                            while (ffmpeg.avcodec_receive_frame(ctx, frame) == 0)
                            {
                                if (frame->data[0] != null && frame->width > 0 && frame->height > 0)
                                    BroadcastYuv(frame);
                                ffmpeg.av_frame_unref(frame);
                            }
                        }
                        else
                        {
                            consecutiveErrors++;
                            if (consecutiveErrors > 30)
                                throw new InvalidOperationException("Too many consecutive decode errors; reconnecting");
                        }

                        ffmpeg.av_packet_unref(pkt);
                    }
                }
                finally
                {
                    ffmpeg.av_frame_free(&frame);
                    ffmpeg.av_packet_free(&pkt);
                }
            }
            finally
            {
                ffmpeg.avcodec_free_context(&ctx);
            }
        }
        finally
        {
            if (fmt != null) ffmpeg.avformat_close_input(&fmt);
        }
    }

    private unsafe void BroadcastYuv(AVFrame* f)
    {
        var frame = CopyYuv(f);
        lock (_subLock)
            foreach (var ch in _subscribers)
                ch.Writer.TryWrite(frame);
    }

    private static unsafe VideoFrame CopyYuv(AVFrame* f)
    {
        int w = f->width, h = f->height;

        if ((AVPixelFormat)f->format == AVPixelFormat.AV_PIX_FMT_NV12)
        {
            var y = new byte[w * h];
            var uv = new byte[w * (h / 2)];
            for (int r = 0; r < h; r++) new Span<byte>(f->data[0] + r * f->linesize[0], w).CopyTo(y.AsSpan(r * w, w));
            for (int r = 0; r < h / 2; r++) new Span<byte>(f->data[1] + r * f->linesize[1], w).CopyTo(uv.AsSpan(r * w, w));
            return new VideoFrame { Width = w, Height = h, Format = VideoPixelFormat.Nv12, Planes = new[] { y, uv }, Strides = new[] { w, w } };
        }

        int hw = w / 2, hh = h / 2;
        var yp = new byte[w * h];
        var up = new byte[hw * hh];
        var vp = new byte[hw * hh];
        for (int r = 0; r < h; r++) new Span<byte>(f->data[0] + r * f->linesize[0], w).CopyTo(yp.AsSpan(r * w, w));
        for (int r = 0; r < hh; r++) new Span<byte>(f->data[1] + r * f->linesize[1], hw).CopyTo(up.AsSpan(r * hw, hw));
        for (int r = 0; r < hh; r++) new Span<byte>(f->data[2] + r * f->linesize[2], hw).CopyTo(vp.AsSpan(r * hw, hw));
        return new VideoFrame { Width = w, Height = h, Format = VideoPixelFormat.Yuv420P, Planes = new[] { yp, up, vp }, Strides = new[] { w, hw, hw } };
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        Stop();
        _cts.Dispose();
    }

    private sealed class SoftRetryException : Exception
    {
        public SoftRetryException(string msg) : base(msg) { }
    }
}
