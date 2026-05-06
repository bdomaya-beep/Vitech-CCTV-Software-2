using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    private void TilesItemsControl_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || DataContext is not LiveViewViewModel vm)
        {
            return;
        }

        var node = e.OriginalSource as DependencyObject;
        while (node is not null)
        {
            if (node is Grid grid && grid.Tag is string tileId)
            {
                var tile = vm.Tiles.FirstOrDefault(t => t.TileId == tileId);
                if (tile is not null && !string.IsNullOrWhiteSpace(tile.CameraId))
                {
                    vm.ZoomTileCommand?.Execute(tile);
                    e.Handled = true;
                }

                return;
            }

            node = VisualTreeHelper.GetParent(node);
        }
    }

    private async void Tile_OnDrop(object sender, DragEventArgs e)
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

        await vm.AssignCameraToTileAsync(tileId, cameraId);
    }
}
