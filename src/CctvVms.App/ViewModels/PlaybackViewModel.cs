using System.Collections.ObjectModel;
using CctvVms.App.Infrastructure;
using CctvVms.Core.Contracts;
using CctvVms.Core.Domain;
using LibVLCSharp.Shared;

namespace CctvVms.App.ViewModels;

public sealed class PlaybackViewModel : ObservableObject
{
    private readonly IDataStoreService _store;
    private readonly IStreamEngine _streamEngine;
    private CameraEntity? _selectedCamera;
    private DateTime _selectedDate = DateTime.Today;
    private double _timelinePosition;
    private string _playbackState = "Stopped";
    private MediaPlayer? _playbackPlayer;

    public PlaybackViewModel(IDataStoreService store, IStreamEngine streamEngine)
    {
        _store = store;
        _streamEngine = streamEngine;

        PlayCommand = new AsyncRelayCommand(PlayAsync, () => SelectedCamera is not null);
        PauseCommand = new RelayCommand(() => PlaybackState = "Paused", () => PlaybackPlayer is not null);
        FastForwardCommand = new RelayCommand(() => TimelinePosition = Math.Min(86400, TimelinePosition + 30));
        RewindCommand = new RelayCommand(() => TimelinePosition = Math.Max(0, TimelinePosition - 30));
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
        set => SetProperty(ref _timelinePosition, value);
    }

    public string PlaybackState
    {
        get => _playbackState;
        set => SetProperty(ref _playbackState, value);
    }

    public MediaPlayer? PlaybackPlayer
    {
        get => _playbackPlayer;
        set => SetProperty(ref _playbackPlayer, value);
    }

    public System.Windows.Input.ICommand PlayCommand { get; }
    public System.Windows.Input.ICommand PauseCommand { get; }
    public System.Windows.Input.ICommand FastForwardCommand { get; }
    public System.Windows.Input.ICommand RewindCommand { get; }

    public async Task InitializeAsync()
    {
        Cameras.Clear();
        foreach (var camera in await _store.GetAllCamerasAsync())
        {
            Cameras.Add(camera);
        }

        SelectedCamera ??= Cameras.FirstOrDefault();
    }

    private async Task PlayAsync()
    {
        if (SelectedCamera is null)
        {
            return;
        }

        var active = await _streamEngine.StartStreamAsync(SelectedCamera, StreamType.Playback);
        PlaybackPlayer = active.MediaPlayer;
        PlaybackState = "Playing";
    }
}
