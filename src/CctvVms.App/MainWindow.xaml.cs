using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using CctvVms.App.ViewModels;
using CctvVms.App.Views.Modules;

namespace CctvVms.App;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        // Initial page is LiveView — already visible by default
    }

    // Called whenever ActiveModule changes on the VM
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActiveModule) && _vm is not null)
        {
            Dispatcher.Invoke(() => SwitchToPage(_vm.ActiveModule));
        }
    }

    private void SwitchToPage(string module)
    {
        var map = new (string Key, UserControl Page)[]
        {
            ("LiveView",      LiveViewPage),
            ("Playback",      PlaybackPage),
            ("DeviceManager", DeviceManagerPage),
            ("Settings",      SettingsPage),
        };

        foreach (var (key, page) in map)
        {
            if (key == module)
            {
                page.Visibility = Visibility.Visible;

                // LiveView: instant — video already rendering, no need to fade
                // Other pages: fade in for a smooth entrance
                if (key != "LiveView")
                {
                    page.Opacity = 0;
                    var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(160)))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    page.BeginAnimation(OpacityProperty, fadeIn);
                }
                else
                {
                    page.Opacity = 1;
                }
            }
            else
            {
                // Stop any running animation before hiding
                page.BeginAnimation(OpacityProperty, null);
                page.Visibility = Visibility.Collapsed;
                page.Opacity = 0;
            }
        }
    }

    private async void AddButton_OnTreeClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var dialog = new CctvVms.App.Views.Dialogs.AddNvrDialog { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Input is null) return;
        var result = await vm.DeviceTree.AddDeviceByInputAsync(dialog.Input);
        await vm.DeviceTree.LoadAsync();
        if (result.Cameras.Count == 0)
            MessageBox.Show(this, result.DiagnosticMessage, "NVR Import Failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void DiscoverButton_OnTreeClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var dialog = new CctvVms.App.Views.Dialogs.DiscoverRangeDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;
        await vm.DeviceTree.DiscoverByRangeAsync(dialog.SubnetOrRange);
    }

    private async void DetachLiveView_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        try
        {
            var detachedVm = await vm.CreateDetachedLiveViewAsync();

            // Set MediaPlayer on detached tiles BEFORE the window is created/shown.
            // At this point no VideoView exists yet, so VideoView.Attach() is a no-op.
            // When window.Show() fires VideoView.Loaded → ForegroundWindow created →
            // MediaPlayer != null → player.Hwnd = fgHwnd called inside Loaded handler.
            // This avoids the race where ContentRendered fires before all 16
            // ForegroundWindow instances have finished their own render passes.
            vm.TransferPlayersToDetached(detachedVm);

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

            window.Closed += async (_, _) =>
            {
                // window.Closed fires after all VideoView.Unloaded events which call
                // Detach(player) → player.Hwnd = 0. RestoreAfterDetachAsync cycles
                // main tiles null→player → Attach → player.Hwnd = mainFgHwnd.
                await vm.RestoreAfterDetachAsync();
                detachedVm.Dispose();
            };

            window.Show();
            // VideoView.Loaded fires for each of the 16 tiles → ForegroundWindow.Show()
            // → since MediaPlayer is already set, player.Hwnd = fgHwnd immediately.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DetachLiveView failed: {ex.Message}");
        }
    }

    private async void NewLiveView_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        try
        {
            var newVm = await vm.CreateNewLiveWindowAsync();

            var layoutRoot = new Grid();
            layoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            layoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var tree = new DeviceTreeControl { DataContext = vm.DeviceTree };
            var live = new LiveViewControl { DataContext = newVm };

            Grid.SetColumn(tree, 0);
            Grid.SetColumn(live, 1);
            layoutRoot.Children.Add(tree);
            layoutRoot.Children.Add(live);

            var window = new Window
            {
                Title = "New Live View",
                Width = 1500,
                Height = 900,
                MinWidth = 1000,
                MinHeight = 700,
                Content = layoutRoot,
                Background = (System.Windows.Media.Brush?)FindResource("SurfaceBrush")
            };

            window.ContentRendered += async (_, _) =>
            {
                // VideoView.Loaded has fired for all tiles; player.Hwnd is set.
                // Now start playback on every camera in the independent engine.
                await newVm.StartAllCameraStreamsAsync();
            };

            window.Closed += (_, _) =>
            {
                // Disposes the owned StreamEngine — stops all RTSP sessions for this window.
                newVm.Dispose();
            };

            window.Show();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NewLiveView failed: {ex.Message}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosed(e);
    }
}
