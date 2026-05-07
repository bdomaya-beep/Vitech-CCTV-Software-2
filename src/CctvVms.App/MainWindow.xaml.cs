using System.Windows;
using System.Windows.Controls;
using CctvVms.App.ViewModels;
using CctvVms.App.Views.Modules;

namespace CctvVms.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void DetachLiveView_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var detachedVm = await vm.CreateDetachedLiveViewAsync();

        var layoutRoot = new Grid();
        layoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        layoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var tree = new DeviceTreeControl { DataContext = vm.DeviceTree };
        var live = new LiveViewControl { DataContext = detachedVm };

        Grid.SetColumn(tree, 0);
        Grid.SetColumn(live, 1);
        layoutRoot.Children.Add(tree);
        layoutRoot.Children.Add(live);

        var window = new Window
        {
            Title = "Detached Live View",
            Width = 1500,
            Height = 900,
            MinWidth = 1000,
            MinHeight = 700,
            Content = layoutRoot,
            Background = (System.Windows.Media.Brush?)FindResource("SurfaceBrush")
        };

        window.Closed += (_, _) => detachedVm.Dispose();
        window.Show();
    }
}