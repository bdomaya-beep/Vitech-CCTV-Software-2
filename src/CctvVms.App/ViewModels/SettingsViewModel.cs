using CctvVms.App.Infrastructure;
using CctvVms.Core.Contracts;
using CctvVms.Core.Streaming;

namespace CctvVms.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly IDataStoreService   _store;
    private readonly StreamEngineOptions _engineOpts;
    private int    _maxMainStreams    = 4;
    private int    _maxActiveDecoders = 24;
    private string _rtspTransport     = "TCP";

    public SettingsViewModel(IDataStoreService store, StreamEngineOptions engineOpts)
    {
        _store      = store;
        _engineOpts = engineOpts;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
    }

    public int MaxMainStreams
    {
        get => _maxMainStreams;
        set => SetProperty(ref _maxMainStreams, value);
    }

    public int MaxActiveDecoders
    {
        get => _maxActiveDecoders;
        set => SetProperty(ref _maxActiveDecoders, value);
    }

    public string RtspTransport
    {
        get => _rtspTransport;
        set
        {
            if (SetProperty(ref _rtspTransport, value))
                _engineOpts.RtspTransport = value.ToLowerInvariant();
        }
    }

    public IReadOnlyList<string> RtspTransportOptions { get; } = new[] { "TCP", "UDP" };

    public System.Windows.Input.ICommand SaveCommand { get; }

    public System.Windows.Input.ICommand CopyTcpRecommendationCommand { get; } =
        new RelayCommand(_ => System.Windows.Clipboard.SetText("rtsp_transport=tcp"));

    public System.Windows.Input.ICommand CopyQueueSizeCommand { get; } =
        new RelayCommand(_ => System.Windows.Clipboard.SetText("Queue size = 1 to 3 frames"));

    public async Task LoadAsync()
    {
        var settings = await _store.GetUserSettingsAsync();

        if (settings.TryGetValue("MaxMainStreams", out var maxMain) && int.TryParse(maxMain, out var pMain))
            MaxMainStreams = pMain;

        if (settings.TryGetValue("MaxActiveDecoders", out var maxDec) && int.TryParse(maxDec, out var pDec))
            MaxActiveDecoders = pDec;

        if (settings.TryGetValue("RtspTransport", out var transport) &&
            (transport == "TCP" || transport == "UDP"))
            RtspTransport = transport;
    }

    private async Task SaveAsync()
    {
        await _store.SaveUserSettingAsync("MaxMainStreams",    MaxMainStreams.ToString());
        await _store.SaveUserSettingAsync("MaxActiveDecoders", MaxActiveDecoders.ToString());
        await _store.SaveUserSettingAsync("RtspTransport",    RtspTransport);

        _engineOpts.MaxMainStreams    = MaxMainStreams;
        _engineOpts.MaxActiveDecoders = MaxActiveDecoders;
        _engineOpts.RtspTransport    = RtspTransport.ToLowerInvariant();
    }
}
