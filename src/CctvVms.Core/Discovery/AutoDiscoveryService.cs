using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CctvVms.Core.Discovery;

public sealed class NetworkDiscoveryEngine
{
    public async Task<List<string>> ScanNetworkRange(string baseIp, CancellationToken cancellationToken = default)
    {
        var results = new List<string>();
        var throttler = new SemaphoreSlim(32, 32);

        var tasks = Enumerable.Range(1, 254).Select(async i =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ip = $"{baseIp}.{i}";

            await throttler.WaitAsync(cancellationToken);
            try
            {
                if (await IsHostAlive(ip, cancellationToken) || await IsServicePortOpen(ip, 37777, cancellationToken))
                {
                    lock (results)
                    {
                        results.Add(ip);
                    }
                }
            }
            finally
            {
                throttler.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results;
    }

    private static async Task<bool> IsHostAlive(string ip, CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, 300);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> IsServicePortOpen(string ip, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(ip, port);
            var timeoutTask = Task.Delay(220, cancellationToken);
            var completed = await Task.WhenAny(connectTask, timeoutTask);
            return completed == connectTask && client.Connected;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class MultiNetworkScanner
{
    private readonly NetworkDiscoveryEngine _engine;

    public MultiNetworkScanner(NetworkDiscoveryEngine engine)
    {
        _engine = engine;
    }

    public async Task<List<string>> DiscoverAllNetworks(List<string> gateways, CancellationToken cancellationToken = default)
    {
        var allDevices = new List<string>();

        var tasks = gateways.Select(async gateway =>
        {
            if (string.IsNullOrWhiteSpace(gateway) || !gateway.Contains('.'))
            {
                return;
            }

            var baseIp = gateway[..gateway.LastIndexOf('.')];
            var devices = await _engine.ScanNetworkRange(baseIp, cancellationToken);

            lock (allDevices)
            {
                allDevices.AddRange(devices);
            }
        });

        await Task.WhenAll(tasks);
        return allDevices.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public sealed class DeviceFilter
{
    private static readonly int[] CandidatePorts = new[] { 37777 };

    public async Task<bool> IsCctvDevice(string ip, CancellationToken cancellationToken = default)
    {
        foreach (var port in CandidatePorts)
        {
            if (await CheckPort(ip, port, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> CheckPort(string ip, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(ip, port);
            var timeoutTask = Task.Delay(250, cancellationToken);
            var completed = await Task.WhenAny(connectTask, timeoutTask);
            return completed == connectTask && client.Connected;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class AutoDiscoveryService
{
    private readonly MultiNetworkScanner _scanner;
    private readonly DeviceFilter _filter;

    public AutoDiscoveryService(MultiNetworkScanner scanner, DeviceFilter filter)
    {
        _scanner = scanner;
        _filter = filter;
    }

    public async Task<List<string>> DiscoverCctvDevices(List<string> gateways, CancellationToken cancellationToken = default)
    {
        var rawDevices = await _scanner.DiscoverAllNetworks(gateways, cancellationToken);
        var result = new List<string>();

        foreach (var ip in rawDevices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _filter.IsCctvDevice(ip, cancellationToken))
            {
                result.Add(ip);
            }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<string> ResolveGateways(string gatewayInput)
    {
        if (!string.IsNullOrWhiteSpace(gatewayInput))
        {
            var items = gatewayInput
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ToGateway)
                .Where(static v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (items.Count > 0)
            {
                return items;
            }
        }

        var fallback = new List<string>();
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                var props = network.GetIPProperties();
                foreach (var gateway in props.GatewayAddresses)
                {
                    var addr = gateway.Address;
                    if (addr.AddressFamily == AddressFamily.InterNetwork)
                    {
                        fallback.Add(addr.ToString());
                    }
                }

                foreach (var uni in props.UnicastAddresses)
                {
                    var addr = uni.Address;
                    if (addr.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var parts = addr.ToString().Split('.');
                        if (parts.Length == 4)
                        {
                            parts[3] = "1";
                            fallback.Add(string.Join('.', parts));
                        }
                    }
                }
            }
            catch
            {
                // Ignore inaccessible interfaces.
            }
        }

        return fallback.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ToGateway(string value)
    {
        var normalized = value.Trim();
        if (normalized.Contains('/'))
        {
            var subnet = normalized.Split('/')[0];
            var parts = subnet.Split('.');
            if (parts.Length == 4)
            {
                parts[3] = "1";
                return string.Join('.', parts);
            }
        }

        if (normalized.Contains('*'))
        {
            return normalized.Replace("*", "1");
        }

        if (IPAddress.TryParse(normalized, out var parsed) && parsed.AddressFamily == AddressFamily.InterNetwork)
        {
            return normalized;
        }

        return string.Empty;
    }
}