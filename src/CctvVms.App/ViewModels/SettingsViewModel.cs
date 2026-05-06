using CctvVms.App.Infrastructure;
using CctvVms.Core.Contracts;

namespace CctvVms.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly IDataStoreService _store;
    private int _maxMainStreams = 4;
    private int _maxActiveDecoders = 24;

    public SettingsViewModel(IDataStoreService store)
    {
        _store = store;
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

    public System.Windows.Input.ICommand SaveCommand { get; }

    public async Task LoadAsync()
    {
        var settings = await _store.GetUserSettingsAsync();

        if (settings.TryGetValue("MaxMainStreams", out var maxMain) && int.TryParse(maxMain, out var parsedMain))
        {
            MaxMainStreams = parsedMain;
        }

        if (settings.TryGetValue("MaxActiveDecoders", out var maxDecoder) && int.TryParse(maxDecoder, out var parsedDecoder))
        {
            MaxActiveDecoders = parsedDecoder;
        }
    }

    private async Task SaveAsync()
    {
        await _store.SaveUserSettingAsync("MaxMainStreams", MaxMainStreams.ToString());
        await _store.SaveUserSettingAsync("MaxActiveDecoders", MaxActiveDecoders.ToString());
    }
}
