using CctvVms.App.Infrastructure;
using CctvVms.Core.Contracts;
using CctvVms.Core.Domain;
using CctvVms.Core.Streaming;
using LibVLCSharp.Shared;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace CctvVms.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private WorkspaceModule _currentModule = WorkspaceModule.LiveView;
    private object? _currentModuleViewModel;
    private LiveViewWorkspaceState? _selectedLiveWorkspace;
    private LiveViewWorkspaceState? _activeLiveWorkspace;
    private bool _switchingWorkspace;
    private readonly LibVLC _libVlc;
    private readonly StreamEngineOptions _streamOptions;

    public MainViewModel(
        DeviceTreeViewModel deviceTree,
        LiveViewViewModel liveView,
        PlaybackViewModel playback,
        DeviceManagerViewModel deviceManager,
        SettingsViewModel settings,
        LibVLC libVlc,
        StreamEngineOptions streamOptions)
    {
        DeviceTree = deviceTree;
        LiveView = liveView;
        Playback = playback;
        DeviceManager = deviceManager;
        Settings = settings;
        _libVlc = libVlc;
        _streamOptions = streamOptions;

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

    public string SystemStatusText => $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | Active streams: {LiveView.Tiles.Count(t => t.MediaPlayer is not null)}";

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

    private void SwitchModule(object? parameter)
    {
        var module = parameter?.ToString();
        switch (module)
        {
            case "LiveView":
                CurrentModule = WorkspaceModule.LiveView;
                CurrentModuleViewModel = LiveView;
                _ = LiveView.RebindAndResumeAsync();
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
                _ = LiveView.RebindAndResumeAsync();
                break;
        }

        RaisePropertyChanged(nameof(SystemStatusText));
        RaisePropertyChanged(nameof(SelectedCameraName));
        RaisePropertyChanged(nameof(SelectedCameraStatus));
        RaisePropertyChanged(nameof(SelectedCameraBitrate));
        RaisePropertyChanged(nameof(SelectedCameraResolution));
        RaisePropertyChanged(nameof(SelectedCameraFps));
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
        if (_switchingWorkspace)
        {
            return;
        }

        if (ReferenceEquals(_activeLiveWorkspace, target))
        {
            return;
        }

        _switchingWorkspace = true;
        try
        {
            await SaveCurrentWorkspaceAsync();
            await LiveView.LoadWorkspaceAsync(target);
            await LiveView.RebindAndResumeAsync();
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
        if (_switchingWorkspace || _activeLiveWorkspace is null)
        {
            return;
        }

        var state = LiveView.CaptureWorkspace(_activeLiveWorkspace.Name);
        _activeLiveWorkspace.Layout = state.Layout;
        _activeLiveWorkspace.CameraIds = state.CameraIds;
        await Task.CompletedTask;
    }

    public async Task<LiveViewViewModel> CreateDetachedLiveViewAsync()
    {
        await SaveCurrentWorkspaceAsync();

        var detachedOptions = new StreamEngineOptions
        {
            MaxActiveDecoders = Math.Max(4, _streamOptions.MaxActiveDecoders / 2),
            MaxMainStreams = Math.Max(1, _streamOptions.MaxMainStreams),
            HealthCheckInterval = _streamOptions.HealthCheckInterval,
            StaleSessionThreshold = _streamOptions.StaleSessionThreshold
        };

        var detachedGpu = new GpuLoadBalancer { MaxGpuStreams = 2 };
        var detachedPool = new StreamPoolManager(_libVlc, detachedOptions, detachedGpu);
        var detachedEngine = new StreamEngine(_libVlc, detachedPool, detachedOptions);
        var detachedView = new LiveViewViewModel(detachedEngine, DeviceTree, ownsStreamEngine: true);

        await detachedView.InitializeAsync();

        var seed = _activeLiveWorkspace ?? LiveWorkspaces.FirstOrDefault();
        if (seed is not null)
        {
            await detachedView.LoadWorkspaceAsync(seed);
        }

        return detachedView;
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
            RaisePropertyChanged(nameof(SystemStatusText));
        }
    }

    private void TilesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(SystemStatusText));
    }
}
