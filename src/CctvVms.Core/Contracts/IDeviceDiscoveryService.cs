using CctvVms.Core.Domain;

namespace CctvVms.Core.Contracts;

public sealed class DiscoveredDevice
{
    public string Name { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public string DeviceType { get; init; } = "NVR";
    public int CameraChannels { get; init; }
    public string VendorHint { get; init; } = "Generic";
}

public interface IDeviceDiscoveryService
{
    Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(string subnetOrRange, CancellationToken cancellationToken = default);
}
