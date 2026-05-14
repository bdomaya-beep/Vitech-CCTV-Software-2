using CctvVms.App.Infrastructure;
using CctvVms.Core.Contracts;
using CctvVms.Core.Domain;
using CctvVms.Core.Streaming;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace CctvVms.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private WorkspaceModule _currentModule = WorkspaceModule.LiveView;
    private object? _currentModuleViewModel;
    private LiveViewWorkspaceState? _selectedLiveWorkspace;
    private LiveViewWorkspaceState? _activeLiveWorkspace;
    private bool _switchingWorkspace;
        
    public MainViewModel(
        DeviceTreeViewModel deviceTree,
        LiveViewViewModel liveView,
        PlaybackViewModel playback,
        DeviceManagerViewModel deviceManager,
        SettingsViewModel settings)
    {
        DeviceTree = deviceTree;
        LiveView = liveView;
        Playback = playback;
        DeviceManager = deviceManager;
        Settings = settings;
        LiveView.PropertyChanged += LiveViewOnPropertyChanged;
        LiveView.Tiles.CollectionChanged += TilesOnCollectionChanged;

        CurrentModuleViewModel = LiveView;
        SwitchModuleCommand = new RelayCommand(SwitchModule);
        CreateLiveWorkspaceCommand = new RelayCommand(_ => _ = CreateLiveWorkspaceAsync());
        RemoveLiveWorkspaceCommand = new RelayCommand(_ => _ = RemoveLiveWorkspaceAsync(), _ => LiveWorkspaces.Count > 1);

        LiveWorkspaces.Add(new LiveViewWorkspaceState { Name = "Live View 1", Layout = "2x2" });
        _selectedLiveWorkspace = LiveWorkspaces[0];
        _activeLiveWorkspace = LiveWorkspaces[0];
    }

    public DeviceTreeViewModel DeviceTree { get; }
    public LiveViewViewModel LiveView { get; }
    public PlaybackViewModel Playback { get; }
    public DeviceManagerViewModel DeviceManager { get; }
    public SettingsViewModel Settings { get; }
    public ObservableCollection<LiveViewWorkspaceState> LiveWorkspaces { get; } = new();

    public WorkspaceModule CurrentModule
    {
        get => _currentModule;
        private set => SetProperty(ref _currentModule, value);
    }

    public object? CurrentModuleViewModel
    {
        get => _currentModuleViewModel;
        private set => SetProperty(ref _currentModuleViewModel, value);
    }

    public LiveViewWorkspaceState? SelectedLiveWorkspace
    {
        get => _selectedLiveWorkspace;
        set
        {
            if (SetProperty(ref _selectedLiveWorkspace, value) && value is not null)
            {
                _ = SwitchLiveWorkspaceAsync(value);
            }
        }
    }

    public string SystemStatusText => $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | Active streams: {LiveView.Tiles.Count(t => t.VideoSource is not null)}";
    public string ActiveModule => _currentModule.ToString();
    public string ActiveModuleTitle => _currentModule switch {
        WorkspaceModule.LiveView => "Live View",
        WorkspaceModule.Playback => "Playback",
        WorkspaceModule.DeviceManager => "Device Manager",
        WorkspaceModule.Settings => "Settings",
        _ => "Live View"
    };

    public string SelectedCameraName => LiveView.SelectedTile?.CameraName ?? "None";
    public string SelectedCameraStatus => LiveView.SelectedTile?.CameraStatusText ?? "Unknown";
    public string SelectedCameraBitrate => LiveView.SelectedTile is null ? "-" : "Adaptive";
    public string SelectedCameraResolution => LiveView.SelectedTile is null ? "-" : (LiveView.SelectedTile.StreamType == StreamType.Main ? "1920x1080" : "640x360");
    public string SelectedCameraFps => LiveView.SelectedTile is null ? "-" : (LiveView.SelectedTile.StreamType == StreamType.Main ? "25" : "12");

    public System.Windows.Input.ICommand SwitchModuleCommand { get; }
    public System.Windows.Input.ICommand CreateLiveWorkspaceCommand { get; }
    public System.Windows.Input.ICommand RemoveLiveWorkspaceCommand { get; }

    public async Task InitializeAsync()
    {
        await DeviceTree.LoadAsync();
        await LiveView.InitializeAsync();
        await Playback.InitializeAsync();
        await DeviceManager.LoadAsync();
        await Settings.LoadAsync();

        if (SelectedLiveWorkspace is not null)
        {
            SelectedLiveWorkspace.Layout = LiveView.CurrentLayout;
            SelectedLiveWorkspace.CameraIds = LiveView.Tiles.Select(t => t.CameraId).ToList();
            _activeLiveWorkspace = SelectedLiveWorkspace;
        }

        DeviceTree.RequestCameraDropCommand = new RelayCommand(parameter =>
        {
            var payload = parameter?.ToString() ?? string.Empty;
            var split = payload.Split('|');
            if (split.Length == 2)
            {
                _ = LiveView.AssignCameraToTileAsync(split[1], split[0]);
            }
        });
    }

    public void SwitchModule(string module) => SwitchModule((object?)module);

    private void SwitchModule(object? parameter)
    {
        var module = parameter?.ToString();
        switch (module)
        {
            case "LiveView":
                CurrentModule = WorkspaceModule.LiveView;
                CurrentModuleViewModel = LiveView;
                break;
            case "Playback":
                CurrentModule = WorkspaceModule.Playback;
                CurrentModuleViewModel = Playback;
                break;
            case "DeviceManager":
                CurrentModule = WorkspaceModule.DeviceManager;
                CurrentModuleViewModel = DeviceManager;
                break;
            case "Settings":
                CurrentModule = WorkspaceModule.Settings;
                CurrentModuleViewModel = Settings;
                break;
            default:
                CurrentModule = WorkspaceModule.LiveView;
                CurrentModuleViewModel = LiveView;
                break;
        }

        RaisePropertyChanged(nameof(SystemStatusText));
        RaisePropertyChanged(nameof(SelectedCameraName));
        RaisePropertyChanged(nameof(SelectedCameraStatus));
        RaisePropertyChanged(nameof(SelectedCameraBitrate));
        RaisePropertyChanged(nameof(SelectedCameraResolution));
        RaisePropertyChanged(nameof(SelectedCameraFps));
        RaisePropertyChanged(nameof(ActiveModule));
        RaisePropertyChanged(nameof(ActiveModuleTitle));
    }

    private async Task CreateLiveWorkspaceAsync()
    {
        await SaveCurrentWorkspaceAsync();

        var next = LiveWorkspaces.Count + 1;
        var workspace = new LiveViewWorkspaceState
        {
            Name = $"Live View {next}",
            Layout = "2x2",
            CameraIds = new List<string>()
        };

        LiveWorkspaces.Add(workspace);
        SelectedLiveWorkspace = workspace;
    }

    private async Task RemoveLiveWorkspaceAsync()
    {
        if (LiveWorkspaces.Count <= 1 || SelectedLiveWorkspace is null)
        {
            return;
        }

        var index = LiveWorkspaces.IndexOf(SelectedLiveWorkspace);
        LiveWorkspaces.Remove(SelectedLiveWorkspace);

        if (index >= LiveWorkspaces.Count)
        {
            index = LiveWorkspaces.Count - 1;
        }

        SelectedLiveWorkspace = LiveWorkspaces[index];
        await Task.CompletedTask;
    }

    private async Task SwitchLiveWorkspaceAsync(LiveViewWorkspaceState target)
    {
        if (_switchingWorkspace) return;
        if (ReferenceEquals(_activeLiveWorkspace, target)) return;

        _switchingWorkspace = true;
        try
        {
            // Save current workspace BEFORE the flag is set Ã¢â‚¬â€ inline so the guard
            // in SaveCurrentWorkspaceAsync cannot block it.
            if (_activeLiveWorkspace is not null)
            {
                var current = LiveView.CaptureWorkspace(_activeLiveWorkspace.Name);
                _activeLiveWorkspace.Layout = current.Layout;
                _activeLiveWorkspace.CameraIds = current.CameraIds;
            }

            await LiveView.LoadWorkspaceAsync(target);
            _activeLiveWorkspace = target;
            RaisePropertyChanged(nameof(SystemStatusText));
        }
        finally
        {
            _switchingWorkspace = false;
        }
    }

    private async Task SaveCurrentWorkspaceAsync()
    {
        if (_switchingWorkspace || _activeLiveWorkspace is null) return;

        var state = LiveView.CaptureWorkspace(_activeLiveWorkspace.Name);
        _activeLiveWorkspace.Layout = state.Layout;
        _activeLiveWorkspace.CameraIds = state.CameraIds;
        await Task.CompletedTask;
    }

    public async Task<LiveViewViewModel> CreateDetachedLiveViewAsync(LiveViewWorkspaceState? workspace = null)
    {
        if (_activeLiveWorkspace is not null)
        {
            var snap = LiveView.CaptureWorkspace(_activeLiveWorkspace.Name);
            _activeLiveWorkspace.Layout = snap.Layout;
            _activeLiveWorkspace.CameraIds = snap.CameraIds;
        }

        var seed = workspace ?? _activeLiveWorkspace ?? LiveWorkspaces.FirstOrDefault();

        // Reuse the same stream engine -- no new RTSP connections, no extra CPU/NVR load.
        var detachedView = new LiveViewViewModel(LiveView.StreamEngine, DeviceTree, ownsStreamEngine: false);
        await detachedView.ApplyLayoutAsync(seed?.Layout ?? LiveView.CurrentLayout);
        detachedView.SeedTileMetadata(LiveView.Tiles);

        return detachedView;
    }

    // Called synchronously BEFORE window.Show().
    // Step 1: null every main tile that has a matching camera in detached.
    //         Ã¢â€ â€™ main VideoView.OnMediaPlayerChanged Ã¢â€ â€™ Detach(player) Ã¢â€ â€™ player.Hwnd=0.
    //         Ã¢â€ â€™ main ForegroundWindow.Attach(null) is now a no-op Ã¢â‚¬â€ stops position-
    //           tracking loop that would otherwise keep calling player.Hwnd=mainFgHwnd.
    // Step 2: assign players to detached tiles.
    //         No VideoView exists yet (window not shown) Ã¢â€ â€™ Attach is a no-op.
    //         When window.Show() later fires VideoView.Loaded Ã¢â€ â€™ ForegroundWindow created
    //         Ã¢â€ â€™ Attach(player) Ã¢â€ â€™ player.Hwnd=detachedFgHwnd Ã¢â‚¬â€ with no competition.
    public void TransferPlayersToDetached(LiveViewViewModel detached)
    {
        var transfers = new List<(VideoTileViewModel MainTile, VideoTileViewModel DetTile, IVideoSource Source)>();
        foreach (var detTile in detached.Tiles.Where(t => !string.IsNullOrWhiteSpace(t.CameraId)))
        {
            var mainTile = LiveView.Tiles.FirstOrDefault(
                t => string.Equals(t.CameraId, detTile.CameraId, StringComparison.OrdinalIgnoreCase));
            var source = mainTile?.VideoSource
                ?? LiveView.StreamEngine.GetActiveStreams()
                       .FirstOrDefault(s => string.Equals(s.CameraId, detTile.CameraId, StringComparison.OrdinalIgnoreCase))
                       ?.VideoSource;
            if (source is null) continue;
            transfers.Add((mainTile!, detTile, source));
        }

        // Step 1: clear main tiles so their ForegroundWindows stop tracking these players.
        foreach (var (mainTile, _, _) in transfers)
            mainTile.VideoSource = null;

        // Step 2: pre-assign players to detached tiles (no VideoView yet Ã¢â€ â€™ Attach no-op).
        foreach (var (_, detTile, player) in transfers)
            detTile.VideoSource = player;
    }

    public async Task<LiveViewViewModel> CreateNewLiveWindowAsync()
    {
        var secondaryOpts = new StreamEngineOptions
        {
            MaxActiveDecoders     = 16,
            MaxMainStreams         = 16,
            HealthCheckInterval   = TimeSpan.FromSeconds(20),
            StaleSessionThreshold = TimeSpan.FromSeconds(60)
        };
        var engine = new StreamEngine(secondaryOpts);
        var vm = new LiveViewViewModel(engine, DeviceTree, ownsStreamEngine: true);

        await vm.ApplyLayoutAsync(LiveView.CurrentLayout);
        vm.SeedTileMetadata(LiveView.Tiles);

        // Streams are started lazily after the window is shown.
        // See NewLiveView_OnClick Ã¢â€ â€™ ContentRendered Ã¢â€ â€™ StartAllCameraStreamsAsync.
        return vm;
    }

    public async Task RestoreAfterDetachAsync()
    {
        // window.Closed fires after all detached VideoView.Unloaded events.
        // Each Unloaded calls Detach(player) Ã¢â€ â€™ player.Hwnd=0.
        // Main tiles already have MediaPlayer=null (set in TransferPlayersToDetached step 1).
        // RebindFromEngineAsync finds main tiles with CameraId but no MediaPlayer,
        // looks up the active session, sets tile.VideoSource=player Ã¢â€ â€™ Attach Ã¢â€ â€™
        // player.Hwnd=mainFgHwnd, restoring video in the main window.
        await LiveView.RebindFromEngineAsync();
    }

    private void LiveViewOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LiveViewViewModel.SelectedTile))
        {
            RaisePropertyChanged(nameof(SelectedCameraName));
            RaisePropertyChanged(nameof(SelectedCameraStatus));
            RaisePropertyChanged(nameof(SelectedCameraBitrate));
            RaisePropertyChanged(nameof(SelectedCameraResolution));
            RaisePropertyChanged(nameof(SelectedCameraFps));
        RaisePropertyChanged(nameof(ActiveModule));
        RaisePropertyChanged(nameof(ActiveModuleTitle));
            RaisePropertyChanged(nameof(SystemStatusText));
        }
    }

    private void TilesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(SystemStatusText));
    }
}





