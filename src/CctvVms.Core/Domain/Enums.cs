namespace CctvVms.Core.Domain;

public enum DeviceStatus
{
    Offline = 0,
    Online = 1,
    Unknown = 2
}

public enum StreamType
{
    Sub = 0,
    Main = 1,
    Playback = 2
}

public enum WorkspaceModule
{
    LiveView = 0,
    Playback = 1,
    DeviceManager = 2,
    Settings = 3
}
