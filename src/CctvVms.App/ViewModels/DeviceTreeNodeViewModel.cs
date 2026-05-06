using System.Collections.ObjectModel;
using CctvVms.App.Infrastructure;
using CctvVms.Core.Domain;

namespace CctvVms.App.ViewModels;

public sealed class DeviceTreeNodeViewModel : ObservableObject
{
    private bool _isExpanded;
    private bool _isSelected;

    public string Id { get; init; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDeviceNode { get; init; }
    public CameraEntity? Camera { get; init; }
    public DeviceEntity? Device { get; init; }
    public DeviceStatus Status { get; set; } = DeviceStatus.Unknown;

    public ObservableCollection<DeviceTreeNodeViewModel> Children { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string StatusText => Status.ToString();
}
