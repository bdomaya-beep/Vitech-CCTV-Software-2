using CctvVms.App.Infrastructure;
using CctvVms.Core.Domain;
using LibVLCSharp.Shared;

namespace CctvVms.App.ViewModels;

public sealed class VideoTileViewModel : ObservableObject
{
    private string _cameraId = string.Empty;
    private string _cameraName = "Empty";
    private bool _isFocused;
    private DeviceStatus _cameraStatus = DeviceStatus.Unknown;
    private StreamType _streamType = StreamType.Sub;
    private MediaPlayer? _mediaPlayer;

    public string TileId { get; init; } = Guid.NewGuid().ToString("N");
    public int Row { get; set; }
    public int Column { get; set; }
    public int RowSpan { get; set; } = 1;
    public int ColumnSpan { get; set; } = 1;

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

    public DeviceStatus CameraStatus
    {
        get => _cameraStatus;
        set
        {
            if (SetProperty(ref _cameraStatus, value))
            {
                RaisePropertyChanged(nameof(CameraStatusText));
            }
        }
    }

    public StreamType StreamType
    {
        get => _streamType;
        set
        {
            if (SetProperty(ref _streamType, value))
            {
                RaisePropertyChanged(nameof(StreamTypeText));
            }
        }
    }

    public MediaPlayer? MediaPlayer
    {
        get => _mediaPlayer;
        set => SetProperty(ref _mediaPlayer, value);
    }

    public string CameraStatusText => CameraStatus.ToString();
    public string StreamTypeText => StreamType.ToString();
}
