namespace CctvVms.Core.Domain;

public sealed class DeviceEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string NvrType { get; set; } = "Generic";
    public DeviceStatus Status { get; set; } = DeviceStatus.Unknown;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class CameraEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DeviceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Channel { get; set; }
    public string RtspMainUrl { get; set; } = string.Empty;
    public string RtspSubUrl { get; set; } = string.Empty;
    public DeviceStatus Status { get; set; } = DeviceStatus.Unknown;
}

public sealed class LayoutEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string LayoutName { get; set; } = "Main View";
    public string LayoutJson { get; set; } = "{}";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class UserSettingEntity
{
    public string SettingKey { get; set; } = string.Empty;
    public string SettingValue { get; set; } = string.Empty;
}

public sealed class PlaybackRecordEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CameraId { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string SourceUri { get; set; } = string.Empty;
}
