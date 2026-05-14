using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CctvVms.Core.Streaming;

namespace CctvVms.App.Rendering;

/// <summary>
/// Streaming pipeline stages 3-5:
///   Stage 3 — Frame Queue     : bounded Channel(YUV, size 2, drop-oldest) owned by decoder
///   Stage 4 — Renderer Thread : YUV → BGR32 on a background thread (not UI)
///   Stage 5 — UI Thread       : WritePixels (memcpy only, no computation)
/// </summary>
public sealed class GlVideoSurface : Decorator
{
    private sealed class BgrFrame
    {
        public readonly int    Width, Height;
        public readonly byte[] Data;
        public BgrFrame(int w, int h, byte[] d) { Width = w; Height = h; Data = d; }
    }

    private readonly Image _img;
    private WriteableBitmap? _bitmap;

    // Stage 4 → Stage 5 handoff: renderer thread writes, UI thread reads (atomic swap)
    private BgrFrame? _latest;

    private IVideoSource?             _source;
    private ChannelReader<VideoFrame>? _reader;
    private CancellationTokenSource?  _cts;

    public GlVideoSurface()
    {
        _img = new Image { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.LowQuality);
        Child = _img;
        Loaded   += (_, _) => CompositionTarget.Rendering += OnTick;
        Unloaded += (_, _) => { CompositionTarget.Rendering -= OnTick; Detach(); };
    }

    /// <summary>Wires up the full pipeline. Call from VideoTileControl on source change.</summary>
    public void AttachSource(IVideoSource? source)
    {
        Detach();
        if (source == null) return;

        _source = source;
        _cts    = new CancellationTokenSource();
        _reader = source.Subscribe();   // Stage 3: subscribe to decoder Frame Queue

        var reader = _reader;
        var cts    = _cts;

        // Stage 4: dedicated Renderer Thread per tile
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var frame in reader.ReadAllAsync(cts.Token))
                {
                    if (!frame.IsValid) continue;
                    var bgr = ToBgr32(frame);                    // off UI thread
                    Interlocked.Exchange(ref _latest, bgr);       // atomic latest-frame swap
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    private void Detach()
    {
        _cts?.Cancel();
        if (_source != null && _reader != null)
            _source.Unsubscribe(_reader);
        _source = null;
        _reader = null;
        _cts    = null;
        Interlocked.Exchange(ref _latest, null);
    }

    // Stage 5: runs on WPF UI thread at ~60fps — only does WritePixels (memcpy)
    private void OnTick(object? sender, EventArgs e)
    {
        var frame = Interlocked.Exchange(ref _latest, null);
        if (frame == null) return;

        if (_bitmap == null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
        {
            _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr32, null);
            _img.Source = _bitmap;
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height),
                            frame.Data, frame.Width * 4, 0);
    }

    // ── YUV → BGR32 conversion (Stage 4, Renderer Thread) ────────────────

    private static unsafe BgrFrame ToBgr32(VideoFrame f)
    {
        int w = f.Width, h = f.Height;
        var bgr = new byte[w * h * 4];
        fixed (byte* dst = bgr)
        {
            if (f.Format == VideoPixelFormat.Nv12) ConvertNv12(f, dst, w, h);
            else                                   ConvertYuv420(f, dst, w, h);
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
                byte* yr  = yBase  + row * ys;
                byte* uvr = uvBase + (row >> 1) * uvs;
                byte* dr  = dst    + row * w * 4;
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
                byte* dr = dst   + row * w * 4;
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
