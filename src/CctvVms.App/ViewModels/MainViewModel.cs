using CctvVms.App.Infrastructure;
using CctvVms.Core.Domain;
using System.Collections.Specialized;
using System.ComponentModel;

namespace CctvVms.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private WorkspaceModule _currentModule = WorkspaceModule.LiveView;
    private object? _currentModuleViewModel;

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
    }

    public DeviceTreeViewModel DeviceTree { get; }
    public LiveViewViewModel LiveView { get; }
    public PlaybackViewModel Playback { get; }
    public DeviceManagerViewModel DeviceManager { get; }
    public SettingsViewModel Settings { get; }

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

    public string SystemStatusText => $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | Active streams: {LiveView.Tiles.Count(t => t.MediaPlayer is not null)}";

    public string SelectedCameraName => LiveView.SelectedTile?.CameraName ?? "None";
    public string SelectedCameraStatus => LiveView.SelectedTile?.CameraStatusText ?? "Unknown";
    public string SelectedCameraBitrate => LiveView.SelectedTile is null ? "-" : "Adaptive";
    public string SelectedCameraResolution => LiveView.SelectedTile is null ? "-" : (LiveView.SelectedTile.StreamType == StreamType.Main ? "1920x1080" : "640x360");
    public string SelectedCameraFps => LiveView.SelectedTile is null ? "-" : (LiveView.SelectedTile.StreamType == StreamType.Main ? "25" : "12");

    public System.Windows.Input.ICommand SwitchModuleCommand { get; }

    public async Task InitializeAsync()
    {
        await DeviceTree.LoadAsync();
        await LiveView.InitializeAsync();
        await Playback.InitializeAsync();
        await DeviceManager.LoadAsync();
        await Settings.LoadAsync();

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
