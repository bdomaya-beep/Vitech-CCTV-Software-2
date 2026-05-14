using System.Threading.Channels;

namespace CctvVms.Core.Streaming;

/// <summary>
/// A running video decoder. Multiple tiles can subscribe; each gets its own channel.
/// </summary>
public interface IVideoSource
{
    ChannelReader<VideoFrame> Subscribe();
    void Unsubscribe(ChannelReader<VideoFrame> reader);
}
