using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CctvVms.App.ViewModels;

namespace CctvVms.App.Views.Modules;

public partial class LiveViewControl : UserControl
{
    public LiveViewControl()
    {
        InitializeComponent();
        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape && DataContext is LiveViewViewModel vm && vm.IsZoomedIn)
            {
                vm.ExitZoomCommand?.Execute(null);
                e.Handled = true;
            }
        };
    }

    private void Tile_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        e.Handled = true;

        if (DataContext is not LiveViewViewModel vm)
        {
            return;
        }

        if (sender is not Border border || border.DataContext is not VideoTileViewModel tile)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(tile.CameraId))
        {
            vm.ZoomTileCommand?.Execute(tile);
        }
    }

    private void Tile_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        e.Handled = true;

        if (DataContext is not LiveViewViewModel vm)
        {
            return;
        }

        if (sender is not Border border || border.DataContext is not VideoTileViewModel tile)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(tile.CameraId))
        {
            vm.ZoomTileCommand?.Execute(tile);
        }
    }

    private void Tile_OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not LiveViewViewModel vm)
        {
            return;
        }

        var cameraId = e.Data.GetData(typeof(string)) as string;
        if (string.IsNullOrWhiteSpace(cameraId))
        {
            return;
        }

        if (sender is not Border border || border.Child is not Grid grid || grid.Tag is not string tileId)
        {
            return;
        }

        vm.BeginAssignCameraToTile(tileId, cameraId);
        e.Handled = true;
    }
}
