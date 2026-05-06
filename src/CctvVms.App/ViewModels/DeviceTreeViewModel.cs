using System.Collections.ObjectModel;
using CctvVms.App.Infrastructure;
using CctvVms.App.Models;
using CctvVms.Core.Contracts;
using CctvVms.Core.Domain;

namespace CctvVms.App.ViewModels;

public sealed class DeviceTreeViewModel : ObservableObject
{
    private readonly IDataStoreService _dataStore;
    private readonly IDeviceDiscoveryService _discovery;
    private readonly INvrConnectionService _nvrConnection;
    private DeviceTreeNodeViewModel? _selectedNode;

    public DeviceTreeViewModel(IDataStoreService dataStore, IDeviceDiscoveryService discovery, INvrConnectionService nvrConnection)
    {
        _dataStore = dataStore;
        _discovery = discovery;
        _nvrConnection = nvrConnection;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        AddDeviceCommand = new AsyncRelayCommand(AddDeviceAsync);
        EditSelectedCommand = new AsyncRelayCommand(EditSelectedAsync, () => SelectedNode is not null);
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => SelectedNode is not null);
        DiscoverCommand = new AsyncRelayCommand(DiscoverAsync);
    }

    public ObservableCollection<DeviceTreeNodeViewModel> RootNodes { get; } = new();

    public DeviceTreeNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (SetProperty(ref _selectedNode, value))
            {
                (EditSelectedCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (DeleteSelectedCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand? RequestCameraDropCommand { get; set; }

    public System.Windows.Input.ICommand RefreshCommand { get; }
    public System.Windows.Input.ICommand AddDeviceCommand { get; }
    public System.Windows.Input.ICommand EditSelectedCommand { get; }
    public System.Windows.Input.ICommand DeleteSelectedCommand { get; }
    public System.Windows.Input.ICommand DiscoverCommand { get; }

    public async Task LoadAsync()
    {
        RootNodes.Clear();

        var devices = await _dataStore.GetDevicesAsync();
        foreach (var device in devices)
        {
            var deviceNode = new DeviceTreeNodeViewModel
            {
                Id = device.Id,
                Name = device.Name,
                Device = device,
                IsDeviceNode = true,
                Status = device.Status,
                IsExpanded = true
            };

            var cameras = await _dataStore.GetCamerasByDeviceAsync(device.Id);
            foreach (var camera in cameras)
            {
                deviceNode.Children.Add(new DeviceTreeNodeViewModel
                {
                    Id = camera.Id,
                    Name = camera.Name,
                    Camera = camera,
                    IsDeviceNode = false,
                    Status = camera.Status
                });
            }

            RootNodes.Add(deviceNode);
        }

    }

    public CameraEntity? FindCamera(string cameraId)
    {
        return RootNodes
            .SelectMany(node => node.Children)
            .Select(child => child.Camera)
            .FirstOrDefault(camera => camera?.Id == cameraId);
    }

    private async Task AddDeviceAsync()
    {
        await AddDeviceByInputAsync(new DeviceConnectionInput
        {
            Name = $"NVR-{DateTime.Now:HHmmss}",
            IpAddress = "192.168.1.100",
            DevicePort = 37777,
            Username = "admin",
            Password = "admin123",
            NvrType = "Dahua",
            MaxChannels = 32
        });

        await LoadAsync();
    }

    public async Task<NvrDevice> AddDeviceByInputAsync(DeviceConnectionInput input)
    {
        var connected = await _nvrConnection.ConnectAndLoadCameras(input.IpAddress, input.Username, input.Password, input.NvrType, input.DevicePort, input.MaxChannels);
        connected.Name = string.IsNullOrWhiteSpace(input.Name) ? connected.Name : input.Name;
        if (connected.Connected && connected.Cameras.Count > 0)
        {
            await PersistConnectedNvrAsync(connected, DeviceStatus.Online);
        }

        return connected;
    }

    private async Task EditSelectedAsync()
    {
        if (SelectedNode?.Device is not null)
        {
            SelectedNode.Device.Name = $"{SelectedNode.Device.Name}-Edited";
            await _dataStore.UpsertDeviceAsync(SelectedNode.Device);
            await LoadAsync();
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (SelectedNode is null)
        {
            return;
        }

        if (SelectedNode.IsDeviceNode && SelectedNode.Device is not null)
        {
            await _dataStore.DeleteDeviceAsync(SelectedNode.Device.Id);
        }

        if (!SelectedNode.IsDeviceNode && SelectedNode.Camera is not null)
        {
            await _dataStore.DeleteCameraAsync(SelectedNode.Camera.Id);
        }

        await LoadAsync();
    }

    private async Task DiscoverAsync()
    {
        await DiscoverByRangeAsync("192.168.1.1,192.168.100.1");
    }

    public async Task DiscoverByRangeAsync(string subnetOrRange)
    {
        var discovered = await _discovery.DiscoverAsync(subnetOrRange);

        foreach (var item in discovered.Where(d => d.DeviceType != "Camera"))
        {
            var connected = await _nvrConnection.ConnectAndLoadCameras(item.IpAddress, "admin", "admin123", item.VendorHint, 37777, 16);
            connected.Name = item.Name;
            if (connected.Connected && connected.Cameras.Count > 0)
            {
                await PersistConnectedNvrAsync(connected, DeviceStatus.Online);
            }
        }

        await LoadAsync();
    }

    private async Task<int> PersistConnectedNvrAsync(NvrDevice connected, DeviceStatus status)
    {
        var existingDevice = (await _dataStore.GetDevicesAsync())
            .FirstOrDefault(d => string.Equals(d.IpAddress, connected.IpAddress, StringComparison.OrdinalIgnoreCase));

        var device = new DeviceEntity
        {
            Id = existingDevice?.Id ?? Guid.NewGuid().ToString("N"),
            Name = connected.Name,
            IpAddress = connected.IpAddress,
            Username = connected.Username,
            Password = connected.Password,
            NvrType = connected.NvrType,
            Status = status
        };

        await _dataStore.UpsertDeviceAsync(device);

        var existingCameras = await _dataStore.GetCamerasByDeviceAsync(device.Id);
        var existingByChannel = existingCameras
            .Where(c => c.Channel > 0)
            .GroupBy(c => c.Channel)
            .ToDictionary(g => g.Key, g => g.First());

        var importedChannels = new HashSet<int>();

        foreach (var camera in connected.Cameras)
        {
            var channel = int.TryParse(camera.Id, out var parsedChannel) ? parsedChannel : 0;
            if (channel > 0)
            {
                importedChannels.Add(channel);
            }

            existingByChannel.TryGetValue(channel, out var existingCamera);

            await _dataStore.UpsertCameraAsync(new CameraEntity
            {
                Id = existingCamera?.Id ?? Guid.NewGuid().ToString("N"),
                DeviceId = device.Id,
                Name = ResolveCameraName(camera.Name, existingCamera?.Name, connected.Name, channel),
                Channel = channel,
                Status = status,
                RtspMainUrl = camera.MainStream,
                RtspSubUrl = camera.SubStream
            });
        }

        foreach (var oldCamera in existingCameras.Where(c => c.Channel > 0 && !importedChannels.Contains(c.Channel)))
        {
            await _dataStore.DeleteCameraAsync(oldCamera.Id);
        }

        return connected.Cameras.Count;
    }

    private static string ResolveCameraName(string importedName, string? existingName, string deviceName, int channel)
    {
        if (!string.IsNullOrWhiteSpace(existingName) && !IsPlaceholderName(existingName))
        {
            return existingName;
        }

        if (!string.IsNullOrWhiteSpace(importedName) && !IsPlaceholderName(importedName))
        {
            return importedName;
        }

        var channelLabel = channel > 0 ? $"CH {channel:00}" : "CH --";
        return $"{deviceName} {channelLabel}";
    }

    private static bool IsPlaceholderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        var value = name.Trim();
        return value.StartsWith("Channel ", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("Camera ", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("CH ", StringComparison.OrdinalIgnoreCase);
    }
}
