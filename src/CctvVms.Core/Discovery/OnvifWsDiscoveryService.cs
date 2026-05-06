using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using CctvVms.Core.Contracts;

namespace CctvVms.Core.Discovery;

public sealed class OnvifWsDiscoveryService : IDeviceDiscoveryService
{
    private const string ProbeMessage = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><e:Envelope xmlns:e=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:w=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\" xmlns:d=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\" xmlns:dn=\"http://www.onvif.org/ver10/network/wsdl\"><e:Header><w:MessageID>uuid:{0}</w:MessageID><w:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To><w:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action></e:Header><e:Body><d:Probe><d:Types>dn:NetworkVideoTransmitter</d:Types></d:Probe></e:Body></e:Envelope>";

    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(string subnetOrRange, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);
        using var udp = new UdpClient(AddressFamily.InterNetwork)
        {
            EnableBroadcast = true,
            MulticastLoopback = false
        };

        udp.Client.ReceiveTimeout = 1200;
        var endpoint = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 3702);
        var payload = Encoding.UTF8.GetBytes(string.Format(ProbeMessage, Guid.NewGuid()));
        await udp.SendAsync(payload, payload.Length, endpoint);

        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < TimeSpan.FromSeconds(2.5))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var receiveTask = udp.ReceiveAsync();
                var completed = await Task.WhenAny(receiveTask, Task.Delay(250, cancellationToken));
                if (completed != receiveTask)
                {
                    continue;
                }

                var packet = receiveTask.Result;
                var xml = Encoding.UTF8.GetString(packet.Buffer);
                var xaddrMatch = Regex.Match(xml, "<XAddrs>(.*?)</XAddrs>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                var scopesMatch = Regex.Match(xml, "<Scopes.*?>(.*?)</Scopes>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                var xaddrs = xaddrMatch.Success ? xaddrMatch.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
                var ip = TryExtractIp(xaddrs) ?? packet.RemoteEndPoint.Address.ToString();
                if (string.IsNullOrWhiteSpace(ip))
                {
                    continue;
                }

                var vendor = InferVendor(scopesMatch.Success ? scopesMatch.Groups[1].Value : string.Empty);
                results[ip] = new DiscoveredDevice
                {
                    Name = string.IsNullOrWhiteSpace(vendor) ? $"ONVIF Device ({ip})" : $"{vendor} ({ip})",
                    IpAddress = ip,
                    DeviceType = "NVR",
                    CameraChannels = 0,
                    VendorHint = string.IsNullOrWhiteSpace(vendor) ? "ONVIF" : vendor
                };
            }
            catch
            {
                break;
            }
        }

        return results.Values.OrderBy(d => d.IpAddress).ToList();
    }

    private static string? TryExtractIp(IEnumerable<string> xaddrs)
    {
        foreach (var xaddr in xaddrs)
        {
            if (!Uri.TryCreate(xaddr, UriKind.Absolute, out var uri))
            {
                continue;
            }

            return uri.Host;
        }

        return null;
    }

    private static string InferVendor(string scopes)
    {
        var value = scopes.ToLowerInvariant();
        if (value.Contains("hikvision") || value.Contains("hik"))
        {
            return "Hikvision";
        }

        if (value.Contains("dahua"))
        {
            return "Dahua";
        }

        if (value.Contains("axis"))
        {
            return "Axis";
        }

        return "ONVIF";
    }
}
