using CctvVms.Core.Contracts;

namespace CctvVms.Core.Streaming;

public sealed class StreamPoolManager : IStreamPoolManager, IDisposable
{
    public int ActiveDecoderCount => 0;
    public int MaxDecoders        => 0;
    public void Dispose() { }
}
