using CctvVms.Core.Domain;

namespace CctvVms.Core.Contracts;

public interface INvrConnectionService
{
    Task<NvrDevice> ConnectAndLoadCameras(string ip, string username, string password, string nvrType = "Dahua", int devicePort = 37777, int maxChannels = 32, CancellationToken cancellationToken = default);
    Task<bool> TestConnection(string ip, string username, string password, string nvrType = "Dahua", int devicePort = 37777, CancellationToken cancellationToken = default);
    (string Main, string Sub) BuildStreamUrls(string ip, string username, string password, int channel, string nvrType, int rtspPort = 554);
    string BuildPlaybackUrl(string ip, string username, string password, int channel, string nvrType, DateTime startUtc, DateTime endUtc, int rtspPort = 554, bool useSubStream = false);
}
