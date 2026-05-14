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
        nvr.Cameras = cameras;

        var serviceOk = await CheckServicePortAsync(ip, devicePort, cancellationToken);
        nvr.Connected = serviceOk || await CheckServicePortAsync(ip, rtspPort, cancellationToken);
        nvr.DiagnosticMessage = nvr.Connected
            ? $"Connected. Imported {cameras.Count} channel placeholders."
            : $"Management port {devicePort} unreachable. Channels built from {ip} — streams will connect when opened.";

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