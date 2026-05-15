using System.Collections.ObjectModel;
using CctvVms.App.Infrastructure;
using CctvVms.Core.Contracts;
using CctvVms.Core.Domain;
using CctvVms.Core.Streaming;

namespace CctvVms.App.ViewModels;

public sealed class PlaybackViewModel : ObservableObject
{
    private readonly IDataStoreService _store;
    private readonly IStreamEngine _streamEngine;
    private readonly INvrConnectionService _nvrConnection;
    private CameraEntity? _selectedCamera;
    private DateTime _selectedDate = DateTime.Today;
    private double _timelinePosition;
    private string _playbackState = "Stopped";
    private IVideoSource? _playbackSource;
    private readonly RelayCommand _pauseCommand;
    private readonly RelayCommand _fastForwardCommand;
    private readonly RelayCommand _rewindCommand;
    private readonly AsyncRelayCommand _seekCommand;

    public PlaybackViewModel(IDataStoreService store, IStreamEngine streamEngine, INvrConnectionService nvrConnection)
    {
        _store = store;
        _streamEngine = streamEngine;
        _nvrConnection = nvrConnection;

        PlayCommand = new AsyncRelayCommand(PlayAsync, () => SelectedCamera is not null);
        _pauseCommand = new RelayCommand(PausePlayback, () => PlaybackSource is not null);
        _fastForwardCommand = new RelayCommand(() => StepTimeline(30), () => SelectedCamera is not null);
        _rewindCommand = new RelayCommand(() => StepTimeline(-30), () => SelectedCamera is not null);
        _seekCommand = new AsyncRelayCommand(SeekIfPlayingAsync, () => SelectedCamera is not null);
        PauseCommand = _pauseCommand;
        FastForwardCommand = _fastForwardCommand;
        RewindCommand = _rewindCommand;
        SeekCommand = _seekCommand;
    }

    public ObservableCollection<CameraEntity> Cameras { get; } = new();

    public CameraEntity? SelectedCamera
    {
        get => _selectedCamera;
        set
        {
            if (SetProperty(ref _selectedCamera, value))
            {
                (PlayCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                _fastForwardCommand.NotifyCanExecuteChanged();
                _rewindCommand.NotifyCanExecuteChanged();
                _seekCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public DateTime SelectedDate
    {
        get => _selectedDate;
        set => SetProperty(ref _selectedDate, value);
    }

    public double TimelinePosition
    {
        get => _timelinePosition;
        set => SetProperty(ref _timelinePosition, Math.Clamp(value, 0, 86400));
    }

    public string PlaybackState
    {
        get => _playbackState;
        set => SetProperty(ref _playbackState, value);
    }

    public IVideoSource? PlaybackSource
    {
        get => _playbackSource;
        set
        {
            if (SetProperty(ref _playbackSource, value))
            {
                _pauseCommand.NotifyCanExecuteChanged();
                _fastForwardCommand.NotifyCanExecuteChanged();
                _rewindCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public System.Windows.Input.ICommand PlayCommand { get; }
    public System.Windows.Input.ICommand PauseCommand { get; }
    public System.Windows.Input.ICommand FastForwardCommand { get; }
    public System.Windows.Input.ICommand RewindCommand { get; }
    public System.Windows.Input.ICommand SeekCommand { get; }

    public async Task InitializeAsync()
    {
        Cameras.Clear();
        foreach (var camera in await _store.GetAllCamerasAsync())
            Cameras.Add(camera);

        SelectedCamera ??= Cameras.FirstOrDefault();
        PlaybackState = SelectedCamera is null ? "No camera selected" : "Ready";
    }

    private async Task PlayAsync()
    {
        if (SelectedCamera is null)
            return;

        await _streamEngine.StopStreamAsync(SelectedCamera.Id);

        var sourceUrl = await ResolvePlaybackSourceAsync(SelectedCamera);
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            PlaybackSource = null;
            PlaybackState = "No playback source found";
            return;
        }

        var playbackCamera = new CameraEntity
        {
            Id = SelectedCamera.Id,
            DeviceId = SelectedCamera.DeviceId,
            Name = SelectedCamera.Name,
            Channel = SelectedCamera.Channel,
            Status = SelectedCamera.Status,
            RtspMainUrl = sourceUrl,
            RtspSubUrl = sourceUrl,
        };

        var active = await _streamEngine.StartStreamAsync(playbackCamera, StreamType.Playback);
        PlaybackSource = active.VideoSource;
        await _streamEngine.BeginPlayAsync(SelectedCamera.Id);
        PlaybackState = "Playing";
    }

    private async Task<string?> ResolvePlaybackSourceAsync(CameraEntity camera)
    {
        var selectedMomentUtc = ToSelectedMomentUtc();
        var dayStartUtc = ToLocalDateBoundaryUtc(SelectedDate.Date);
        var dayEndUtc = ToLocalDateBoundaryUtc(SelectedDate.Date.AddDays(1));
        var record = await ResolvePlaybackRecordAsync(camera.Id, dayStartUtc, dayEndUtc, selectedMomentUtc);
        if (!string.IsNullOrWhiteSpace(record?.SourceUri))
            return record.SourceUri;

        var device = (await _store.GetDevicesAsync())
            .FirstOrDefault(d => d.Id == camera.DeviceId);

        if (device is null || string.IsNullOrWhiteSpace(device.IpAddress) || camera.Channel <= 0)
            return null;

        var startUtc = selectedMomentUtc;
        var endUtc = selectedMomentUtc.AddMinutes(30);
        return _nvrConnection.BuildPlaybackUrl(device.IpAddress, device.Username, device.Password, camera.Channel, device.NvrType, startUtc, endUtc);
    }

    private async Task<PlaybackRecordEntity?> ResolvePlaybackRecordAsync(string cameraId, DateTime dayStartUtc, DateTime dayEndUtc, DateTime selectedMomentUtc)
    {
        var records = await _store.GetPlaybackRecordsAsync(cameraId, dayStartUtc, dayEndUtc);
        return records
            .FirstOrDefault(r => r.StartUtc <= selectedMomentUtc && selectedMomentUtc < r.EndUtc)
            ?? records.FirstOrDefault(r => r.StartUtc > selectedMomentUtc)
            ?? records.LastOrDefault();
    }

    private void StepTimeline(double seconds)
    {
        TimelinePosition = Math.Clamp(TimelinePosition + seconds, 0, 86400);
        if (PlaybackSource is not null)
            _ = PlayAsync();
        else
            PlaybackState = "Ready";
    }

    private DateTime ToSelectedMomentUtc()
    {
        var localMoment = SelectedDate.Date.AddSeconds(Math.Clamp(TimelinePosition, 0, 86400));
        return ToLocalDateBoundaryUtc(localMoment);
    }

    private static DateTime ToLocalDateBoundaryUtc(DateTime value)
    {
        var local = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Local)
            : value.ToLocalTime();
        return local.ToUniversalTime();
    }

    private async Task SeekIfPlayingAsync()
    {
        if (PlaybackSource is not null)
            await PlayAsync();
    }

    private void PausePlayback()
    {
        if (SelectedCamera is null || PlaybackSource is null)
            return;

        _ = _streamEngine.StopStreamAsync(SelectedCamera.Id);
        PlaybackSource = null;
        PlaybackState = "Paused";
    }
}