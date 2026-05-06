using System.Net.Sockets;
using CctvVms.Core.Contracts;

namespace CctvVms.Core.Discovery;

public sealed class EndpointDiscoveryService : IDeviceDiscoveryService
{
    private static readonly int[] CandidatePorts = new[] { 80, 554, 8000, 8080, 37777 };

    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(string subnetOrRange, CancellationToken cancellationToken = default)
    {
        var results = new List<DiscoveredDevice>();
        foreach (var ip in ExpandRange(subnetOrRange))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await HasOpenEndpointAsync(ip, cancellationToken))
            {
                continue;
            }

            results.Add(new DiscoveredDevice
            {
                Name = $"IP Device ({ip})",
                IpAddress = ip,
                DeviceType = "NVR",
                CameraChannels = 0,
                VendorHint = "Generic"
            });

            if (results.Count >= 32)
            {
                break;
            }
        }

        return results;
    }

    private static async Task<bool> HasOpenEndpointAsync(string ip, CancellationToken cancellationToken)
    {
        foreach (var port in CandidatePorts)
        {
            using var client = new TcpClient();
            try
            {
                var connectTask = client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(180, cancellationToken);
                var completed = await Task.WhenAny(connectTask, timeoutTask);
                if (completed == connectTask && client.Connected)
                {
                    return true;
                }
            }
            catch
            {
                // ignore closed ports
            }
        }

        return false;
    }

    private static IEnumerable<string> ExpandRange(string subnetOrRange)
    {
        if (string.IsNullOrWhiteSpace(subnetOrRange))
        {
            yield break;
        }

        var normalized = subnetOrRange.Trim();
        if (!normalized.Contains('/'))
        {
            foreach (var ip in Enumerable.Range(1, 254).Select(i => normalized.Replace("*", i.ToString())))
            {
                yield return ip;
            }

            yield break;
        }

        var subnet = normalized.Split('/')[0];
        var parts = subnet.Split('.');
        if (parts.Length != 4)
        {
            yield break;
        }

        var basePrefix = string.Join('.', parts.Take(3));
        foreach (var ip in Enumerable.Range(1, 254).Select(i => $"{basePrefix}.{i}"))
        {
            yield return ip;
        }
    }
}
