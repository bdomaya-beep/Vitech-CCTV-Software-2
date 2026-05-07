using System.Threading;

namespace CctvVms.Core.Streaming;

/// <summary>
/// Tracks how many hardware-decoded streams are active and enforces a cap.
/// When the cap is reached, callers should fall back to software decoding.
/// </summary>
public sealed class GpuLoadBalancer
{
    private int _activeGpuStreams;

    public int MaxGpuStreams { get; set; } = 16;
    public int ActiveGpuStreams => _activeGpuStreams;

    public bool CanUseHardware() => _activeGpuStreams < MaxGpuStreams;

    public void Allocate() => Interlocked.Increment(ref _activeGpuStreams);

    public void Release()
    {
        // Guard against spurious over-releases
        int current;
        do
        {
            current = _activeGpuStreams;
            if (current <= 0) return;
        }
        while (Interlocked.CompareExchange(ref _activeGpuStreams, current - 1, current) != current);
    }
}
