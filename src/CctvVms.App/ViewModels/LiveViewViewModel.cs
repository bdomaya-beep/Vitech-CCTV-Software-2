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
    private CancellationTokenSource _zoomCts = new();

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
        ExitZoomCommand = new AsyncRelayCommand(ExitZoomAsync);
    }

    public ObservableCollection<VideoTileViewModel> Tiles { get; } = new();
    public ObservableCollection<VideoTileViewModel> ZoomTiles { get; } = new();
    internal IStreamEngine StreamEngine => _streamEngine;


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

    public void BeginZoomTile(VideoTileViewModel tile) => _ = ZoomTileAsync(tile);

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
                var existing = _streamEngine.GetActiveStreams().FirstOrDefault(s => s.CameraId == camera.Id);
                bool needsPlay = existing == null || existing.StreamType == StreamType.Main;
                var stream = existing?.StreamType == StreamType.Main
                    ? await Task.Run(async () => await _streamEngine.SwitchStreamAsync(camera, StreamType.Sub))
                    : await Task.Run(async () => await _streamEngine.StartStreamAsync(camera, StreamType.Sub));

                // Assign player to tile FIRST so VideoView binds its HWND, THEN start playback.
                tile.MediaPlayer = stream.MediaPlayer;
                tile.StreamType = stream.StreamType;
                SelectedTile = tile;

                // Wait for the Render pass that creates the ForegroundWindow HWND, then wait for Background
                // so LayoutUpdated (player.Hwnd = hwnd) has definitely fired before Play() is called.
                await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                // Only start playback for new sessions or when stopped; reusing an active sub-stream avoids restarting it.
                if (needsPlay || !stream.MediaPlayer.IsPlaying)
                {
                    await _streamEngine.BeginPlayAsync(camera.Id);
                }
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
                    await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
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

    internal async Task ApplyLayoutAsync(string layout)
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

    private static StreamType ComputeAdaptiveStreamType(VideoTileViewModel tile)
    {
        return StreamType.Sub;
    }

    private async Task ZoomTileAsync(object? parameter)
    {
        if (parameter is not VideoTileViewModel tile || string.IsNullOrWhiteSpace(tile.CameraId))
            return;

        var camera = _deviceTree.FindCamera(tile.CameraId);
        if (camera is null) return;

        var prevCts = _zoomCts;
        _zoomCts = new CancellationTokenSource();
        var ct = _zoomCts.Token;
        try { prevCts.Cancel(); } catch { }

        _zoomedTile = tile;

        // Show the overlay immediately with "Connectingâ€¦" so the user gets instant feedback.
        ZoomTiles.Clear();
        var zoomedTile = new VideoTileViewModel
        {
            TileId = tile.TileId,
            CameraId = tile.CameraId,
            CameraName = tile.CameraName,
            CameraStatus = tile.CameraStatus,
            Row = 0, Column = 0, RowSpan = 1, ColumnSpan = 1,
            StreamType = StreamType.Main,
            IsDeploying = true
        };
        ZoomTiles.Add(zoomedTile);
        IsZoomedIn = true;
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

        if (ct.IsCancellationRequested) return;

        try
        {
            // Stop sub-stream, start main-stream RTSP connection.
            // StopStreamAsync runs Player.Stop() inside Task.Run so the UI stays responsive.
            var mainStream = await _streamEngine.SwitchStreamAsync(camera, StreamType.Main, ct);
            if (ct.IsCancellationRequested) return;

            // HWND already exists (overlay has been Visible since IsZoomedIn = true).
            // Assign player â†’ VideoView calls SetHwnd â†’ VLC renders to the overlay.
            zoomedTile.MediaPlayer = mainStream.MediaPlayer;
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await _streamEngine.BeginPlayAsync(camera.Id, ct);
            zoomedTile.IsDeploying = false;
        }
        catch (OperationCanceledException) { }
        catch
        {
            zoomedTile.IsDeploying = false;
        }
    }

    private async Task ExitZoomAsync()
    {
        try { _zoomCts.Cancel(); } catch { }

        var tileToRestore = _zoomedTile;
        _zoomedTile = null;

        // Close overlay immediately â€” grid reappears, zoomed tile shows "Connectingâ€¦"
        // while the sub-stream reconnects.
        IsZoomedIn = false;
        ZoomTiles.Clear();

        if (tileToRestore is null || string.IsNullOrWhiteSpace(tileToRestore.CameraId))
            return;

        var camera = _deviceTree.FindCamera(tileToRestore.CameraId);
        if (camera is null) return;

        tileToRestore.IsDeploying = true;
        tileToRestore.MediaPlayer = null;

        try
        {
            var subStream = await _streamEngine.SwitchStreamAsync(camera, StreamType.Sub);
            tileToRestore.MediaPlayer = subStream.MediaPlayer;
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await _streamEngine.BeginPlayAsync(camera.Id);
            tileToRestore.IsDeploying = false;
        }
        catch
        {
            tileToRestore.IsDeploying = false;
        }
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

    public async Task PopulateFromDeviceAsync(DeviceTreeNodeViewModel deviceNode)
    {
        var cameras = deviceNode.Children
            .Where(c => !c.IsDeviceNode && c.Camera is not null)
            .Select(c => c.Camera!)
            .ToList();

        if (cameras.Count == 0) return;

        // Pick the smallest layout that fits all cameras.
        var layout = cameras.Count switch
        {
            1         => "1x1",
            <= 4      => "2x2",
            <= 9      => "3x3",
            _         => "4x4"
        };

        await ApplyLayoutAsync(layout);

        // Assign cameras to tiles sequentially up to the tile count.
        var tileCount = Math.Min(cameras.Count, Tiles.Count);
        var tasks = new List<Task>();
        for (var i = 0; i < tileCount; i++)
        {
            var tile = Tiles[i];
            var camera = cameras[i];
            tasks.Add(AssignCameraToTileAsync(tile.TileId, camera.Id));
        }
        await Task.WhenAll(tasks);
    }

    public void SeedTileMetadata(IReadOnlyList<VideoTileViewModel> sourceTiles)
    {
        for (var i = 0; i < Math.Min(Tiles.Count, sourceTiles.Count); i++)
        {
            var src = sourceTiles[i];
            var dst = Tiles[i];
            dst.CameraId     = src.CameraId;
            dst.CameraName   = src.CameraName;
            dst.CameraStatus = src.CameraStatus;
            dst.StreamType   = src.StreamType;
        }
    }

    public async Task RebindFromEngineAsync()
    {
        foreach (var tile in Tiles.Where(t => !string.IsNullOrWhiteSpace(t.CameraId) && t.MediaPlayer is null))
        {
            var stream = _streamEngine.GetActiveStreams().FirstOrDefault(s => s.CameraId == tile.CameraId);
            if (stream?.MediaPlayer is null) continue;
            tile.MediaPlayer = stream.MediaPlayer;
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        }
    }


    // Phase 1 (parallel, off UI): acquire all stream sessions concurrently.
    // Phase 2 (single UI dispatch): assign all MediaPlayers at once.
    // Phase 3 (one render pass): let VideoView ForegroundWindow set all HWNDs.
    // Phase 4 (parallel, off UI): begin playback for all streams at once.
    // This replaces 16x individual dispatches + 16x render passes with 2 total.
    public async Task StartAllCameraStreamsAsync()
    {
        var activeTiles = Tiles.Where(t => !string.IsNullOrWhiteSpace(t.CameraId)).ToList();
        if (activeTiles.Count == 0) return;

        // Phase 1: acquire sessions in parallel on thread pool â€” pool.Acquire is in-memory,
        // but forcing off UI thread prevents synchronous SemaphoreSlim completions from
        // blocking the WPF message loop.
        using var connectGate = new SemaphoreSlim(4, 4);
        var results = await Task.WhenAll(activeTiles.Select(tile => Task.Run(async () =>
        {
            var camera = _deviceTree.FindCamera(tile.CameraId);
            if (camera is null) return (tile: tile, info: (ActiveStreamInfo?)null);
            await connectGate.WaitAsync();
            try
            {
                var info = await _streamEngine.StartStreamAsync(camera, StreamType.Sub);
                return (tile: tile, info: (ActiveStreamInfo?)info);
            }
            catch { return (tile: tile, info: (ActiveStreamInfo?)null); }
            finally { connectGate.Release(); }
        })));

        // Phase 2: assign all MediaPlayers in a single UI dispatch.
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            foreach (var (tile, info) in results)
            {
                if (info is null) continue;
                tile.MediaPlayer = info.MediaPlayer;
                tile.StreamType  = info.StreamType;
            }
        });

        // Phase 3: one render pass so VideoView.ForegroundWindow sets all HWNDs.
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

        // Phase 4: begin playback for all streams in parallel on thread pool.
        await Task.WhenAll(results
            .Where(r => r.Item2 is not null)
            .Select(r => Task.Run(() => _streamEngine.BeginPlayAsync(r.Item1.CameraId))));
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

