using System.Diagnostics;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CctvVms.Core.Domain;
using CctvVms.Core.Streaming;

namespace CctvVms.App.Rendering;

/// <summary>
/// Streaming pipeline stages 3-5:
///   Stage 3 - Frame Queue     : bounded Channel(YUV, size 2, drop-oldest) owned by decoder
///   Stage 4 - Renderer Thread : YUV to BGR32 on a background thread (not UI)
///   Stage 5 - UI Thread       : WritePixels (memcpy only, no computation)
/// </summary>
public sealed class GlVideoSurface : Decorator
{
    private sealed class BgrFrame
    {
        public readonly int Width;
        public readonly int Height;
        public byte[] Data;

        public BgrFrame(int w, int h, byte[] d)
        {
            Width = w;
            Height = h;
            Data = d;
        }

        public void Release() => Data = Array.Empty<byte>();
    }

    private readonly Image _img;
    private WriteableBitmap? _bitmap;

    // Stage 4 -> Stage 5 handoff: renderer thread writes, UI thread reads (atomic swap)
    private BgrFrame? _latest;
    private static readonly long SubStreamFrameIntervalTicks = (long)(Stopwatch.Frequency / 15.0);

    private IVideoSource? _source;
    private ChannelReader<VideoFrame>? _reader;
    private CancellationTokenSource? _cts;
    private bool _isRenderingAttached;
    private long _lastAcceptedFrameTick;

    public GlVideoSurface()
    {
        _img = new Image { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.LowQuality);
        Child = _img;
        Loaded += (_, _) =>
        {
            EnsureRenderingAttached();
            EnsureReaderAttached();
        };
        Unloaded += (_, _) =>
        {
            EnsureRenderingDetached();
            StopReader();
        };
    }

    /// <summary>Wires up the full pipeline. Call from VideoTileControl on source change.</summary>
    public void AttachSource(IVideoSource? source)
    {
        if (ReferenceEquals(_source, source) && _reader != null)
            return;

        _source = source;
        _lastAcceptedFrameTick = 0;
        StopReader();

        if (source == null)
        {
            _bitmap = null;
            _img.Source = null;
            return;
        }

        if (IsLoaded)
        {
            EnsureRenderingAttached();
            EnsureReaderAttached();
        }
    }

    private void EnsureReaderAttached()
    {
        if (_source == null || _reader != null)
            return;

        _cts = new CancellationTokenSource();
        _reader = _source.Subscribe();

        var reader = _reader;
        var cts = _cts;
        bool limitToGridFps = _source is RtspVideoDecoder decoder && decoder.StreamType == StreamType.Sub;

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var frame in reader.ReadAllAsync(cts.Token))
                {
                    if (!frame.IsValid)
                        continue;

                    if (limitToGridFps && !ShouldAcceptSubStreamFrame())
                        continue;

                    var bgr = ToBgr32(frame);
                    Interlocked.Exchange(ref _latest, bgr)?.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private bool ShouldAcceptSubStreamFrame()
    {
        var now = Stopwatch.GetTimestamp();
        var last = Volatile.Read(ref _lastAcceptedFrameTick);
        if (last != 0 && now - last < SubStreamFrameIntervalTicks)
            return false;

        Interlocked.Exchange(ref _lastAcceptedFrameTick, now);
        return true;
    }

    private void StopReader()
    {
        _cts?.Cancel();
        if (_source != null && _reader != null)
            _source.Unsubscribe(_reader);

        _reader = null;
        _cts = null;
        Interlocked.Exchange(ref _latest, null)?.Release();
    }

    private void EnsureRenderingAttached()
    {
        if (_isRenderingAttached)
            return;

        CompositionTarget.Rendering += OnTick;
        _isRenderingAttached = true;
    }

    private void EnsureRenderingDetached()
    {
        if (!_isRenderingAttached)
            return;

        CompositionTarget.Rendering -= OnTick;
        _isRenderingAttached = false;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var frame = Interlocked.Exchange(ref _latest, null);
        if (frame == null)
            return;

        try
        {
            if (_bitmap == null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
            {
                _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr32, null);
                _img.Source = _bitmap;
            }

            _bitmap.WritePixels(
                new Int32Rect(0, 0, frame.Width, frame.Height),
                frame.Data,
                frame.Width * 4,
                0);
        }
        finally
        {
            frame.Release();
        }
    }

    private static unsafe BgrFrame ToBgr32(VideoFrame f)
    {
        int w = f.Width, h = f.Height;
        var bgr = new byte[w * h * 4];
        fixed (byte* dst = bgr)
        {
            if (f.Format == VideoPixelFormat.Nv12)
                ConvertNv12(f, dst, w, h);
            else
                ConvertYuv420(f, dst, w, h);
        }
        return new BgrFrame(w, h, bgr);
    }

    private static unsafe void ConvertNv12(VideoFrame f, byte* dst, int w, int h)
    {
        fixed (byte* yBase = f.Planes[0], uvBase = f.Planes[1])
        {
            int ys = f.Strides[0], uvs = f.Strides[1];
            for (int row = 0; row < h; row++)
            {
                byte* yr = yBase + row * ys;
                byte* uvr = uvBase + (row >> 1) * uvs;
                byte* dr = dst + row * w * 4;
                for (int col = 0; col < w; col++)
                    YuvToBgr(yr[col], uvr[col & ~1], uvr[(col & ~1) + 1], dr + col * 4);
            }
        }
    }

    private static unsafe void ConvertYuv420(VideoFrame f, byte* dst, int w, int h)
    {
        fixed (byte* yBase = f.Planes[0], uBase = f.Planes[1], vBase = f.Planes[2])
        {
            int ys = f.Strides[0], us = f.Strides[1];
            for (int row = 0; row < h; row++)
            {
                byte* yr = yBase + row * ys;
                byte* ur = uBase + (row >> 1) * us;
                byte* vr = vBase + (row >> 1) * us;
                byte* dr = dst + row * w * 4;
                for (int col = 0; col < w; col++)
                    YuvToBgr(yr[col], ur[col >> 1], vr[col >> 1], dr + col * 4);
            }
        }
    }

    private static unsafe void YuvToBgr(int y, int u, int v, byte* px)
    {
        int c = 298 * (y - 16) + 128;
        int r = (c + 409 * (v - 128)) >> 8;
        int g = (c - 100 * (u - 128) - 208 * (v - 128)) >> 8;
        int b = (c + 516 * (u - 128)) >> 8;
        px[0] = (byte)(b < 0 ? 0 : b > 255 ? 255 : b);
        px[1] = (byte)(g < 0 ? 0 : g > 255 ? 255 : g);
        px[2] = (byte)(r < 0 ? 0 : r > 255 ? 255 : r);
        px[3] = 255;
    }
}