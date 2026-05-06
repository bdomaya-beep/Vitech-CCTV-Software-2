using System.Collections.ObjectModel;
using CctvVms.App.Infrastructure;
using CctvVms.Core.Contracts;
using CctvVms.Core.Domain;

namespace CctvVms.App.ViewModels;

public sealed class DeviceManagerViewModel : ObservableObject
{
    private readonly IDataStoreService _store;
    private readonly INvrConnectionService _nvrConnection;
    private DeviceEntity? _selectedDevice;
    private string _connectionTestResult = "Idle";

    public DeviceManagerViewModel(IDataStoreService store, INvrConnectionService nvrConnection)
    {
        _store = store;
        _nvrConnection = nvrConnection;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        RemoveCommand = new AsyncRelayCommand(RemoveAsync, () => SelectedDevice is not null);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => SelectedDevice is not null);
    }

    public ObservableCollection<DeviceEntity> Devices { get; } = new();

    public DeviceEntity? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                (RemoveCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (TestConnectionCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string ConnectionTestResult
    {
        get => _connectionTestResult;
        set => SetProperty(ref _connectionTestResult, value);
    }

    public System.Windows.Input.ICommand RefreshCommand { get; }
    public System.Windows.Input.ICommand RemoveCommand { get; }
    public System.Windows.Input.ICommand TestConnectionCommand { get; }

    public async Task LoadAsync()
    {
        Devices.Clear();
        foreach (var device in await _store.GetDevicesAsync())
        {
            Devices.Add(device);
        }
    }

    private async Task RemoveAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        await _store.DeleteDeviceAsync(SelectedDevice.Id);
        await LoadAsync();
    }

    private async Task TestConnectionAsync()
    {
        if (SelectedDevice is null)
        {
            ConnectionTestResult = "Select a device first.";
            return;
        }

        ConnectionTestResult = "Testing...";
        var ok = await _nvrConnection.TestConnection(SelectedDevice.IpAddress, SelectedDevice.Username, SelectedDevice.Password, SelectedDevice.NvrType, 37777);
        ConnectionTestResult = ok
            ? $"Connected to {SelectedDevice.IpAddress}:37777"
            : $"Failed to connect {SelectedDevice.IpAddress}:37777";
    }
}
