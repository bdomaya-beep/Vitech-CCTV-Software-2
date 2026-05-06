using System.Net.NetworkInformation;
using CctvVms.Core.Contracts;

namespace CctvVms.Core.Discovery;

public sealed class LanRangeDiscoveryService : IDeviceDiscoveryService
{
    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(string subnetOrRange, CancellationToken cancellationToken = default)
    {
        var output = new List<DiscoveredDevice>();
        var targets = ExpandRange(subnetOrRange);

        foreach (var ip in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var ping = new Ping();
            try
            {
                var response = await ping.SendPingAsync(ip, 60);
                if (response.Status == IPStatus.Success)
                {
                    output.Add(new DiscoveredDevice
                    {
                        Name = $"Discovered-{ip}",
                        IpAddress = ip,
                        DeviceType = "Unknown",
                        CameraChannels = 0
                    });
                }
            }
            catch
            {
                // Ignore ping failures.
            }

            if (output.Count >= 32)
            {
                break;
            }
        }

        return output;
    }

    private static IEnumerable<string> ExpandRange(string subnetOrRange)
    {
        if (string.IsNullOrWhiteSpace(subnetOrRange))
        {
            return Enumerable.Empty<string>();
        }

        var normalized = subnetOrRange.Trim();
        if (!normalized.Contains('/'))
        {
            return Enumerable.Range(1, 15).Select(i => normalized.Replace("*", i.ToString()));
        }

        var subnet = normalized.Split('/')[0];
        var parts = subnet.Split('.');
        if (parts.Length != 4)
        {
            return Enumerable.Empty<string>();
        }

        var basePrefix = string.Join('.', parts.Take(3));
        return Enumerable.Range(1, 32).Select(i => $"{basePrefix}.{i}");
    }
}
