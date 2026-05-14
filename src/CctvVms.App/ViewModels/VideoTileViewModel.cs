using CctvVms.App.Infrastructure;
using CctvVms.Core.Domain;
using CctvVms.Core.Streaming;

namespace CctvVms.App.ViewModels;

public sealed class VideoTileViewModel : ObservableObject
{
    private string       _cameraId     = string.Empty;
    private string       _cameraName   = "Empty";
    private bool         _isFocused;
    private bool         _isDeploying;
    private DeviceStatus _cameraStatus = DeviceStatus.Unknown;
    private StreamType   _streamType   = StreamType.Sub;
    private IVideoSource? _videoSource;

    public string TileId   { get; init; } = Guid.NewGuid().ToString("N");
    public int    Row      { get; set; }
    public int    Column   { get; set; }
    public int    RowSpan  { get; set; } = 1;
    public int    ColumnSpan { get; set; } = 1;

    public string CameraId
    {
        get => _cameraId;
        set => SetProperty(ref _cameraId, value);
    }

    public string CameraName
    {
        get => _cameraName;
        set => SetProperty(ref _cameraName, value);
    }

    public bool IsFocused
    {
        get => _isFocused;
        set => SetProperty(ref _isFocused, value);
    }

    public bool IsDeploying
    {
        get => _isDeploying;
        set => SetProperty(ref _isDeploying, value);
    }

    public DeviceStatus CameraStatus
    {
        get => _cameraStatus;
        set
        {
            if (SetProperty(ref _cameraStatus, value))
                RaisePropertyChanged(nameof(CameraStatusText));
        }
    }

    public StreamType StreamType
    {
        get => _streamType;
        set
        {
            if (SetProperty(ref _streamType, value))
                RaisePropertyChanged(nameof(StreamTypeText));
        }
    }

    public IVideoSource? VideoSource
    {
        get => _videoSource;
        set => SetProperty(ref _videoSource, value);
    }

    public string CameraStatusText => CameraStatus.ToString();
    public string StreamTypeText   => StreamType.ToString();
}
