using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CctvVms.Core.Contracts;
using CctvVms.Core.Domain;
using System.Net.Sockets;

namespace CctvVms.Core.Discovery;

public sealed class NvrConnectionService : INvrConnectionService
{
    public async Task<NvrDevice> ConnectAndLoadCameras(string ip, string username, string password, string nvrType = "Dahua", int devicePort = 37777, int maxChannels = 32, CancellationToken cancellationToken = default)
    {
        return await ConnectAndLoadCamerasWithRtsp(ip, username, password, nvrType, devicePort, 554, maxChannels, cancellationToken);
    }

    public async Task<NvrDevice> ConnectAndLoadCamerasWithRtsp(string ip, string username, string password, string nvrType, int devicePort, int rtspPort, int maxChannels, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nvr = new NvrDevice
        {
            IpAddress = ip,
            Username = username,
            Password = password,
            Name = $"NVR ({ip})",
            NvrType = nvrType,
            RtspPort = rtspPort
        };

        var cameras = BuildChannels(ip, username, password, nvrType, maxChannels, rtspPort);
        await TryApplyChannelNamesAsync(cameras, ip, username, password, nvrType, devicePort, cancellationToken);
        nvr.Cameras = cameras;

        var serviceOk = await CheckServicePortAsync(ip, devicePort, cancellationToken);
        nvr.Connected = serviceOk || await CheckServicePortAsync(ip, rtspPort, cancellationToken);
        nvr.DiagnosticMessage = nvr.Connected
            ? $"Connected. Imported {cameras.Count} channels."
            : $"Management port {devicePort} unreachable. Channels built from {ip} - streams will connect when opened.";

        return nvr;
    }

    public async Task<bool> TestConnection(string ip, string username, string password, string nvrType = "Dahua", int devicePort = 37777, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        return await CheckServicePortAsync(ip, devicePort, cancellationToken);
    }

    public (string Main, string Sub) BuildStreamUrls(string ip, string username, string password, int channel, string nvrType, int rtspPort = 554)
    {
        return BuildChannelUrls(ip, username, password, channel, nvrType, rtspPort);
    }

    public string BuildPlaybackUrl(string ip, string username, string password, int channel, string nvrType, DateTime startUtc, DateTime endUtc, int rtspPort = 554, bool useSubStream = false)
    {
        var credentialPrefix = $"{Uri.EscapeDataString(username)}:{Uri.EscapeDataString(password)}@";
        var vendor = NormalizeVendor(nvrType);

        if (vendor == "Hikvision")
        {
            var streamSuffix = useSubStream ? "02" : "01";
            return $"rtsp://{credentialPrefix}{ip}:{rtspPort}/Streaming/tracks/{channel}{streamSuffix}?starttime={startUtc:yyyyMMdd'T'HHmmss'Z'}&endtime={endUtc:yyyyMMdd'T'HHmmss'Z'}";
        }

        if (vendor == "Dahua")
        {
            var subtype = useSubStream ? 1 : 0;
            var startLocal = startUtc.ToLocalTime();
            var endLocal = endUtc.ToLocalTime();
            return $"rtsp://{credentialPrefix}{ip}:{rtspPort}/cam/playback?channel={channel}&subtype={subtype}&starttime={startLocal:yyyy_MM_dd_HH_mm_ss}&endtime={endLocal:yyyy_MM_dd_HH_mm_ss}";
        }

        return $"rtsp://{credentialPrefix}{ip}:{rtspPort}/ch{channel}/main";
    }

    private static List<Camera> BuildChannels(string ip, string user, string pass, string nvrType, int maxChannels, int rtspPort = 554)
    {
        var scanLimit = Math.Min(Math.Max(maxChannels, 1), ResolveMaxChannels(nvrType));
        var list = new List<Camera>();

        for (var channel = 1; channel <= scanLimit; channel++)
        {
            var urls = BuildChannelUrls(ip, user, pass, channel, nvrType, rtspPort);
            list.Add(new Camera
            {
                Id = channel.ToString(),
                Name = $"Channel {channel:00}",
                MainStream = urls.Main,
                SubStream = urls.Sub
            });
        }

        return list
            .OrderBy(c => int.TryParse(c.Id, out var id) ? id : int.MaxValue)
            .ToList();
    }

    private async Task TryApplyChannelNamesAsync(List<Camera> cameras, string ip, string username, string password, string nvrType, int devicePort, CancellationToken cancellationToken)
    {
        try
        {
            var names = await FetchChannelNamesAsync(ip, username, password, nvrType, devicePort, cancellationToken);
            if (names.Count == 0)
                return;

            foreach (var camera in cameras)
            {
                if (!int.TryParse(camera.Id, out var channel))
                    continue;

                if (!names.TryGetValue(channel, out var fetchedName))
                    continue;

                if (!string.IsNullOrWhiteSpace(fetchedName))
                    camera.Name = fetchedName.Trim();
            }
        }
        catch
        {
            // Keep placeholder names if the vendor title endpoint is unavailable.
        }
    }

    private async Task<Dictionary<int, string>> FetchChannelNamesAsync(string ip, string username, string password, string nvrType, int devicePort, CancellationToken cancellationToken)
    {
        var vendor = NormalizeVendor(nvrType);
        return vendor switch
        {
            "Dahua" => await FetchDahuaChannelNamesAsync(ip, username, password, devicePort, cancellationToken),
            "Hikvision" => await FetchHikvisionChannelNamesAsync(ip, username, password, devicePort, cancellationToken),
            _ => new Dictionary<int, string>()
        };
    }

    private async Task<Dictionary<int, string>> FetchDahuaChannelNamesAsync(string ip, string username, string password, int devicePort, CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, string>();
        var body = await TryGetAsync(ip, username, password, devicePort, new[] { 80, 8080 }, "/cgi-bin/configManager.cgi?action=getConfig&name=ChannelTitle", cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
            return result;

        foreach (Match match in Regex.Matches(body, @"ChannelTitle\[(\d+)\]\.Name=(.*)"))
        {
            if (!int.TryParse(match.Groups[1].Value, out var zeroBasedChannel))
                continue;

            var name = WebUtility.UrlDecode(match.Groups[2].Value).Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            result[zeroBasedChannel + 1] = name;
        }

        return result;
    }

    private async Task<Dictionary<int, string>> FetchHikvisionChannelNamesAsync(string ip, string username, string password, int devicePort, CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, string>();
        var body = await TryGetAsync(ip, username, password, devicePort, new[] { 80, 8080 }, "/ISAPI/System/Video/inputs/channels", cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
            return result;

        var document = XDocument.Parse(body);
        foreach (var channelElement in document.Descendants().Where(e => e.Name.LocalName.Contains("VideoInputChannel")))
        {
            var idText = channelElement.Elements().FirstOrDefault(e => e.Name.LocalName == "id")?.Value;
            var nameText = channelElement.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value;
            if (!int.TryParse(idText, out var channel) || string.IsNullOrWhiteSpace(nameText))
                continue;

            result[channel] = nameText.Trim();
        }

        return result;
    }

    private async Task<string?> TryGetAsync(string ip, string username, string password, int preferredPort, IEnumerable<int> fallbackPorts, string path, CancellationToken cancellationToken)
    {
        var ports = new[] { preferredPort }.Concat(fallbackPorts).Distinct();

        foreach (var port in ports)
        {
            try
            {
                using var handler = new HttpClientHandler
                {
                    Credentials = new NetworkCredential(username, password),
                    PreAuthenticate = true,
                    UseDefaultCredentials = false,
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };

                using var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(3)
                };

                var uri = new Uri($"http://{ip}:{port}{path}");
                using var response = await client.GetAsync(uri, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    continue;

                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch
            {
                // Try next candidate port.
            }
        }

        return null;
    }

    private static int ResolveMaxChannels(string nvrType)
    {
        var vendor = NormalizeVendor(nvrType);
        if (vendor is "Hikvision" or "Dahua" or "ONVIF")
        {
            return 32;
        }

        return 16;
    }

    private static (string Main, string Sub) BuildChannelUrls(string ip, string user, string pass, int channel, string nvrType, int rtspPort)
    {
        var credentialPrefix = $"{Uri.EscapeDataString(user)}:{Uri.EscapeDataString(pass)}@";

        var vendor = NormalizeVendor(nvrType);

        if (vendor == "Hikvision")
        {
            return (
                $"rtsp://{credentialPrefix}{ip}:{rtspPort}/Streaming/Channels/{channel}01",
                $"rtsp://{credentialPrefix}{ip}:{rtspPort}/Streaming/Channels/{channel}02");
        }

        if (vendor == "Dahua")
        {
            return (
                $"rtsp://{credentialPrefix}{ip}:{rtspPort}/cam/realmonitor?channel={channel}&subtype=0",
                $"rtsp://{credentialPrefix}{ip}:{rtspPort}/cam/realmonitor?channel={channel}&subtype=1");
        }

        return (
            $"rtsp://{credentialPrefix}{ip}:{rtspPort}/ch{channel}/main",
            $"rtsp://{credentialPrefix}{ip}:{rtspPort}/ch{channel}/sub");
    }

    private static async Task<bool> CheckServicePortAsync(string ip, int port, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(ip, port);
            var timeoutTask = Task.Delay(500, cancellationToken);
            var completed = await Task.WhenAny(connectTask, timeoutTask);
            return completed == connectTask && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeVendor(string nvrType)
    {
        var type = (nvrType ?? string.Empty).Trim().ToLowerInvariant();
        if (type.Contains("hik"))
        {
            return "Hikvision";
        }

        if (type.Contains("dahua"))
        {
            return "Dahua";
        }

        if (type.Contains("onvif"))
        {
            return "ONVIF";
        }

        return "Generic";
    }
}
