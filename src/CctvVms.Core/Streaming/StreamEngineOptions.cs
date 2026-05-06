namespace CctvVms.Core.Streaming;

public sealed class StreamEngineOptions
{
    public int MaxActiveDecoders { get; set; } = 24;
    public int MaxMainStreams { get; set; } = 4;
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan StaleSessionThreshold { get; set; } = TimeSpan.FromSeconds(15);
}
