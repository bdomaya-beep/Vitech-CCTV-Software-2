using System.Collections.ObjectModel;
using CctvVms.App.Infrastructure;
using CctvVms.Core.Contracts;
using CctvVms.Core.Domain;

namespace CctvVms.App.ViewModels;

public sealed class LiveViewViewModel : ObservableObject
{
    private readonly IStreamEngine _streamEngine;
    private readonly DeviceTreeViewModel _deviceTree;
    private int _rows = 2;
    private int _columns = 2;
    private VideoTileViewModel? _selectedTile;
    private bool _isZoomedIn;
    private VideoTileViewModel? _zoomedTile;

    public LiveViewViewModel(IStreamEngine streamEngine, DeviceTreeViewModel deviceTree)
    {
        _streamEngine = streamEngine;
        _deviceTree = deviceTree;

        SetLayoutCommand = new RelayCommand(parameter =>
        {
            var layout = parameter?.ToString() ?? "2x2";
            ApplyLayout(layout);
        });

        FocusTileCommand = new AsyncRelayCommand(FocusTileAsync);
        ClearTileCommand = new AsyncRelayCommand(ClearTileAsync);
        ZoomTileCommand = new AsyncRelayCommand(ZoomTileAsync);
        ExitZoomCommand = new RelayCommand(ExitZoom);
    }

    public ObservableCollection<VideoTileViewModel> Tiles { get; } = new();
    public ObservableCollection<VideoTileViewModel> ZoomTiles { get; } = new();

    public int Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    public int Columns
    {
        get => _columns;
        private set => SetProperty(ref _columns, value);
    }

    public bool IsZoomedIn
    {
        get => _isZoomedIn;
        set => SetProperty(ref _isZoomedIn, value);
    }

    public VideoTileViewModel? SelectedTile
    {
        get => _selectedTile;
        set
        {
            if (SetProperty(ref _selectedTile, value))
            {
                RaisePropertyChanged(nameof(SelectedCameraName));
                RaisePropertyChanged(nameof(SelectedCameraStatus));
            }
        }
    }

    public string SelectedCameraName => SelectedTile?.CameraName ?? "None";
    public string SelectedCameraStatus => SelectedTile?.CameraStatusText ?? "Unknown";

    public System.Windows.Input.ICommand SetLayoutCommand { get; }
    public System.Windows.Input.ICommand FocusTileCommand { get; }
    public System.Windows.Input.ICommand ClearTileCommand { get; }
    public System.Windows.Input.ICommand ZoomTileCommand { get; }
    public System.Windows.Input.ICommand ExitZoomCommand { get; }

    public Task InitializeAsync()
    {
        ApplyLayout("2x2");
        return Task.CompletedTask;
    }

    public async Task AssignCameraToTileAsync(string tileId, string cameraId)
    {
        var tile = Tiles.FirstOrDefault(t => t.TileId == tileId);
        var camera = _deviceTree.FindCamera(cameraId);

        if (tile is null || camera is null)
        {
            return;
        }

        var previousCameraId = tile.CameraId;

        var sourceTile = Tiles.FirstOrDefault(t =>
            t.TileId != tile.TileId &&
            string.Equals(t.CameraId, camera.Id, StringComparison.OrdinalIgnoreCase));

        if (sourceTile is not null)
        {
            tile.CameraId = sourceTile.CameraId;
            tile.CameraName = sourceTile.CameraName;
            tile.CameraStatus = sourceTile.CameraStatus;
            tile.MediaPlayer = sourceTile.MediaPlayer;
            tile.StreamType = sourceTile.StreamType;

            ClearTileUi(sourceTile);

            if (!string.IsNullOrWhiteSpace(previousCameraId)
                && !Tiles.Any(t => t.TileId != tile.TileId && string.Equals(t.CameraId, previousCameraId, StringComparison.OrdinalIgnoreCase)))
            {
                await _streamEngine.StopStreamAsync(previousCameraId);
            }

            SelectedTile = tile;
            return;
        }

        if (!string.IsNullOrWhiteSpace(previousCameraId) && !string.Equals(previousCameraId, camera.Id, StringComparison.OrdinalIgnoreCase))
        {
            var usedElsewhere = Tiles.Any(t => t.TileId != tile.TileId && string.Equals(t.CameraId, previousCameraId, StringComparison.OrdinalIgnoreCase));
            if (!usedElsewhere)
            {
                await _streamEngine.StopStreamAsync(previousCameraId);
            }
        }

        tile.CameraId = camera.Id;
        tile.CameraName = camera.Name;
        tile.CameraStatus = camera.Status;

        var streamType = ComputeAdaptiveStreamType(tile);
        var stream = await _streamEngine.StartStreamAsync(camera, streamType);
        tile.MediaPlayer = stream.MediaPlayer;
        tile.StreamType = stream.StreamType;

        SelectedTile = tile;
    }

    private void ApplyLayout(string layout)
    {
        (Rows, Columns) = layout switch
        {
            "1x1" => (1, 1),
            "2x2" => (2, 2),
            "3x3" => (3, 3),
            "4x4" => (4, 4),
            _ => (2, 2)
        };

        Tiles.Clear();

        for (var row = 0; row < Rows; row++)
        {
            for (var col = 0; col < Columns; col++)
            {
                Tiles.Add(new VideoTileViewModel
                {
                    Row = row,
                    Column = col
                });
            }
        }
    }

    private async Task FocusTileAsync(object? parameter)
    {
        if (parameter is not VideoTileViewModel tile || string.IsNullOrWhiteSpace(tile.CameraId))
        {
            return;
        }

        foreach (var item in Tiles)
        {
            item.IsFocused = item.TileId == tile.TileId;
        }

        var camera = _deviceTree.FindCamera(tile.CameraId);
        if (camera is null)
        {
            return;
        }

        var focused = await _streamEngine.SwitchStreamAsync(camera, StreamType.Main);
        tile.MediaPlayer = focused.MediaPlayer;
        tile.StreamType = focused.StreamType;
        SelectedTile = tile;

        var nonFocusedTiles = Tiles.Where(t => t.TileId != tile.TileId && !string.IsNullOrWhiteSpace(t.CameraId)).ToList();
        foreach (var other in nonFocusedTiles)
        {
            var otherCamera = _deviceTree.FindCamera(other.CameraId);
            if (otherCamera is null)
            {
                continue;
            }

            var downgraded = await _streamEngine.SwitchStreamAsync(otherCamera, StreamType.Sub);
            other.MediaPlayer = downgraded.MediaPlayer;
            other.StreamType = downgraded.StreamType;
            other.IsFocused = false;
        }
    }

    private async Task ClearTileAsync(object? parameter)
    {
        if (parameter is not VideoTileViewModel tile || string.IsNullOrWhiteSpace(tile.CameraId))
        {
            return;
        }

        await _streamEngine.StopStreamAsync(tile.CameraId);
        ClearTileUi(tile);
    }

    private StreamType ComputeAdaptiveStreamType(VideoTileViewModel tile)
    {
        if (tile.IsFocused)
        {
            return StreamType.Main;
        }

        var areaWeight = tile.ColumnSpan * tile.RowSpan;
        return areaWeight >= 4 ? StreamType.Main : StreamType.Sub;
    }

    private async Task ZoomTileAsync(object? parameter)
    {
        if (parameter is not VideoTileViewModel tile || string.IsNullOrWhiteSpace(tile.CameraId))
        {
            return;
        }

        var camera = _deviceTree.FindCamera(tile.CameraId);
        if (camera is null)
        {
            return;
        }

        _zoomedTile = tile;
        IsZoomedIn = true;

        ZoomTiles.Clear();
        var zoomedTile = new VideoTileViewModel
        {
            TileId = tile.TileId,
            CameraId = tile.CameraId,
            CameraName = tile.CameraName,
            CameraStatus = tile.CameraStatus,
            Row = 0,
            Column = 0,
            RowSpan = 1,
            ColumnSpan = 1
        };

        var mainStream = await _streamEngine.SwitchStreamAsync(camera, StreamType.Main);
        zoomedTile.MediaPlayer = mainStream.MediaPlayer;
        zoomedTile.StreamType = mainStream.StreamType;
        ZoomTiles.Add(zoomedTile);

        foreach (var other in Tiles.Where(t => t.TileId != tile.TileId && !string.IsNullOrWhiteSpace(t.CameraId)))
        {
            var otherCamera = _deviceTree.FindCamera(other.CameraId);
            if (otherCamera is not null)
            {
                var subStream = await _streamEngine.SwitchStreamAsync(otherCamera, StreamType.Sub);
                other.MediaPlayer = subStream.MediaPlayer;
                other.StreamType = subStream.StreamType;
            }
        }
    }

    private void ExitZoom()
    {
        IsZoomedIn = false;
        ZoomTiles.Clear();
        _zoomedTile = null;
    }

    private static void ClearTileUi(VideoTileViewModel tile)
    {
        tile.CameraId = string.Empty;
        tile.CameraName = "Empty";
        tile.MediaPlayer = null;
        tile.CameraStatus = DeviceStatus.Unknown;
        tile.StreamType = StreamType.Sub;
    }
}
