using CctvVms.Core.Contracts;

namespace CctvVms.Core.Discovery;

public sealed class CompositeDiscoveryService : IDeviceDiscoveryService
{
    private readonly OnvifWsDiscoveryService _onvif;
    private readonly AutoDiscoveryService _autoDiscovery;

    public CompositeDiscoveryService(OnvifWsDiscoveryService onvif, AutoDiscoveryService autoDiscovery)
    {
        _onvif = onvif;
        _autoDiscovery = autoDiscovery;
    }

    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(string subnetOrRange, CancellationToken cancellationToken = default)
    {
        var gateways = AutoDiscoveryService.ResolveGateways(subnetOrRange);
        var autoTask = _autoDiscovery.DiscoverCctvDevices(gateways, cancellationToken);
        var onvifTask = _onvif.DiscoverAsync(subnetOrRange, cancellationToken);
        await Task.WhenAll(autoTask, onvifTask);

        var autoDevices = autoTask.Result
            .Select(ip => new DiscoveredDevice
            {
                Name = $"CCTV Device ({ip})",
                IpAddress = ip,
                DeviceType = "NVR",
                CameraChannels = 0,
                VendorHint = "Dahua"
            })
            .ToList();

        return autoDevices
            .Concat(onvifTask.Result)
            .GroupBy(d => d.IpAddress)
            .Select(group => group.OrderByDescending(d => d.VendorHint != "Generic").First())
            .OrderBy(d => d.IpAddress)
            .ToList();
    }
}
