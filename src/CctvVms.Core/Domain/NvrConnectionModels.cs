namespace CctvVms.Core.Domain;

public sealed class NvrDevice
{
    public string IpAddress { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NvrType { get; set; } = "Generic";
    public int RtspPort { get; set; } = 554;
    public bool Connected { get; set; }
    public string DiagnosticMessage { get; set; } = string.Empty;
    public List<Camera> Cameras { get; set; } = new();
}

public sealed class Camera
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MainStream { get; set; } = string.Empty;
    public string SubStream { get; set; } = string.Empty;
}
