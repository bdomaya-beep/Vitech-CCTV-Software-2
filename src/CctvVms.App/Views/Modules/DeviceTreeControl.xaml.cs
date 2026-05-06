using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CctvVms.App.ViewModels;
using CctvVms.App.Views.Dialogs;

namespace CctvVms.App.Views.Modules;

public partial class DeviceTreeControl : UserControl
{
    public DeviceTreeControl()
    {
        InitializeComponent();
    }

    private void TreeView_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is DeviceTreeViewModel vm && e.NewValue is DeviceTreeNodeViewModel node)
        {
            vm.SelectedNode = node;
        }
    }

    private void TreeView_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (DataContext is not DeviceTreeViewModel vm || vm.SelectedNode?.Camera is null)
        {
            return;
        }

        DragDrop.DoDragDrop(this, vm.SelectedNode.Camera.Id, DragDropEffects.Copy);
    }

    private async void AddButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DeviceTreeViewModel vm)
        {
            return;
        }

        var dialog = new AddNvrDialog
        {
            Owner = Window.GetWindow(this)
        };

        var accepted = dialog.ShowDialog();
        if (accepted != true || dialog.Input is null)
        {
            return;
        }

        var connected = await vm.AddDeviceByInputAsync(dialog.Input);
        await vm.LoadAsync();

        if (connected.Cameras.Count == 0)
        {
            MessageBox.Show(
                Window.GetWindow(this),
            connected.DiagnosticMessage,
            "NVR Import Failed",
                MessageBoxButton.OK,
            MessageBoxImage.Warning);
        }
    }

    private async void DiscoverButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DeviceTreeViewModel vm)
        {
            return;
        }

        var dialog = new DiscoverRangeDialog
        {
            Owner = Window.GetWindow(this)
        };

        var accepted = dialog.ShowDialog();
        if (accepted != true)
        {
            return;
        }

        await vm.DiscoverByRangeAsync(dialog.SubnetOrRange);
    }
}
