namespace CctvVms.App.Models;

public sealed class DeviceConnectionInput
{
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string AddMode { get; set; } = "IP/Domain Name";
    public int DevicePort { get; set; } = 37777;
    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "admin123";
    public string NvrType { get; set; } = "Dahua";
    public int MaxChannels { get; set; } = 32;
}
