using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CctvVms.App.Infrastructure;
using CctvVms.Core.Contracts;
using CctvVms.Core.Domain;

namespace CctvVms.App.ViewModels;

public sealed class LiveViewViewModel : ObservableObject, IDisposable
{
    private readonly IStreamEngine _streamEngine;
    private readonly DeviceTreeViewModel _deviceTree;
    private readonly bool _ownsStreamEngine;
    private readonly SemaphoreSlim _deployLimiter = new(3, 3);
    private int _rows = 2;
    private int _columns = 2;
    private VideoTileViewModel? _selectedTile;
    private bool _isZoomedIn;
    private VideoTileViewModel? _zoomedTile;

    public LiveViewViewModel(IStreamEngine streamEngine, DeviceTreeViewModel deviceTree, bool ownsStreamEngine = false)
    {
        _streamEngine = streamEngine;
        _deviceTree = deviceTree;
        _ownsStreamEngine = ownsStreamEngine;

        SetLayoutCommand = new AsyncRelayCommand(async parameter =>
        {
            var layout = parameter?.ToString() ?? "2x2";
            await ApplyLayoutAsync(layout);
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
        return ApplyLayoutAsync("2x2");
    }

    public string CurrentLayout => $"{Rows}x{Columns}";

    public LiveViewWorkspaceState CaptureWorkspace(string name)
    {
        return new LiveViewWorkspaceState
        {
            Name = name,
            Layout = CurrentLayout,
            CameraIds = Tiles.Select(t => t.CameraId).ToList()
        };
    }

    public async Task LoadWorkspaceAsync(LiveViewWorkspaceState state)
    {
        await ApplyLayoutAsync(state.Layout);

        for (var i = 0; i < Tiles.Count && i < state.CameraIds.Count; i++)
        {
            var cameraId = state.CameraIds[i];
            if (string.IsNullOrWhiteSpace(cameraId))
            {
                continue;
            }

            await AssignCameraToTileAsync(Tiles[i].TileId, cameraId);
        }
    }

    public async Task RebindAndResumeAsync()
    {
        var activeTiles = Tiles
            .Where(t => !string.IsNullOrWhiteSpace(t.CameraId) && t.MediaPlayer is not null)
            .ToList();

        foreach (var tile in activeTiles)
        {
            var player = tile.MediaPlayer;
            tile.MediaPlayer = null;
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            tile.MediaPlayer = player;
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await _streamEngine.BeginPlayAsync(tile.CameraId);
        }

        if (IsZoomedIn)
        {
            var activeZoomTiles = ZoomTiles
                .Where(t => !string.IsNullOrWhiteSpace(t.CameraId) && t.MediaPlayer is not null)
                .ToList();

            foreach (var tile in activeZoomTiles)
            {
                var player = tile.MediaPlayer;
                tile.MediaPlayer = null;
                await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                tile.MediaPlayer = player;
                await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                await _streamEngine.BeginPlayAsync(tile.CameraId);
            }
        }
    }

    public void BeginAssignCameraToTile(string tileId, string cameraId)
    {
        _ = AssignCameraToTileAsync(tileId, cameraId);
    }

    public async Task AssignCameraToTileAsync(string tileId, string cameraId)
    {
        var tile = Tiles.FirstOrDefault(t => t.TileId == tileId);
        var camera = _deviceTree.FindCamera(cameraId);

        if (tile is null || camera is null)
        {
            return;
        }

        tile.IsDeploying = true;

        var previousCameraId = tile.CameraId;

        var sourceTile = Tiles.FirstOrDefault(t =>
            t.TileId != tile.TileId &&
            string.Equals(t.CameraId, camera.Id, StringComparison.OrdinalIgnoreCase));

        await _deployLimiter.WaitAsync();
        try
        {
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

            try
            {
                var streamType = ComputeAdaptiveStreamType(tile);
                var stream = await Task.Run(async () => await _streamEngine.StartStreamAsync(camera, streamType));

                // Assign player to tile FIRST so VideoView binds its HWND, THEN start playback.
                tile.MediaPlayer = stream.MediaPlayer;
                tile.StreamType = stream.StreamType;
                SelectedTile = tile;

                // Yield to the WPF render pass so VideoView has attached the HWND to the player.
                await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                await _streamEngine.BeginPlayAsync(camera.Id);
            }
            catch
            {
                // Retry once with explicit sub-stream for intermittent/high-bitrate failures.
                try
                {
                    var subStream = await Task.Run(async () => await _streamEngine.StartStreamAsync(camera, StreamType.Sub));
                    tile.MediaPlayer = subStream.MediaPlayer;
                    tile.StreamType = subStream.StreamType;
                    SelectedTile = tile;

                    await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                    await _streamEngine.BeginPlayAsync(camera.Id);
                }
                catch
                {
                    tile.CameraStatus = DeviceStatus.Offline;
                    tile.MediaPlayer = null;
                    tile.StreamType = StreamType.Sub;
                }
            }
        }
        finally
        {
            tile.IsDeploying = false;
            _deployLimiter.Release();
        }
    }

    private async Task ApplyLayoutAsync(string layout)
    {
        var activeCameraIds = Tiles
            .Where(t => !string.IsNullOrWhiteSpace(t.CameraId))
            .Select(t => t.CameraId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var cameraId in activeCameraIds)
        {
            await _streamEngine.StopStreamAsync(cameraId);
        }

        IsZoomedIn = false;
        ZoomTiles.Clear();
        _zoomedTile = null;

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
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await _streamEngine.BeginPlayAsync(camera.Id);

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
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await _streamEngine.BeginPlayAsync(otherCamera.Id);
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

        // Show zoom immediately with current player so double-click always responds.
        zoomedTile.MediaPlayer = tile.MediaPlayer;
        zoomedTile.StreamType = tile.StreamType;
        ZoomTiles.Add(zoomedTile);

        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

        try
        {
            var mainStream = await _streamEngine.SwitchStreamAsync(camera, StreamType.Main);
            zoomedTile.MediaPlayer = mainStream.MediaPlayer;
            zoomedTile.StreamType = mainStream.StreamType;
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await _streamEngine.BeginPlayAsync(camera.Id);
        }
        catch
        {
            // Keep current stream in zoom if main-stream switch fails under load.
        }

        foreach (var other in Tiles.Where(t => t.TileId != tile.TileId && !string.IsNullOrWhiteSpace(t.CameraId)))
        {
            var otherCamera = _deviceTree.FindCamera(other.CameraId);
            if (otherCamera is not null)
            {
                try
                {
                    var subStream = await _streamEngine.SwitchStreamAsync(otherCamera, StreamType.Sub);
                    other.MediaPlayer = subStream.MediaPlayer;
                    other.StreamType = subStream.StreamType;
                    await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                    await _streamEngine.BeginPlayAsync(otherCamera.Id);
                }
                catch
                {
                    // Best effort only; keep existing stream if downgrade fails.
                }
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
        tile.IsDeploying = false;
    }

    public void Dispose()
    {
        if (_ownsStreamEngine && _streamEngine is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

public sealed class LiveViewWorkspaceState
{
    public string Name { get; set; } = "Live View";
    public string Layout { get; set; } = "2x2";
    public List<string> CameraIds { get; set; } = new();
}
