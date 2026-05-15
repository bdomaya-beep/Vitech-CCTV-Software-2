using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CctvVms.App.ViewModels;

namespace CctvVms.App.Views.Modules;

public partial class LiveViewControl : UserControl
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hmod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int n);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string? s);

    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int SM_CXDOUBLECLK = 36;
    private const int SM_CYDOUBLECLK = 37;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData, flags, time;
        public IntPtr dwExtraInfo;
    }

    private HookProc? _hookProc;
    private IntPtr _mouseHook = IntPtr.Zero;
    private uint _lastClickTick;
    private int _lastClickX;
    private int _lastClickY;
    private Panel? _tilesOriginalParent;
    private int _tilesOriginalIndex = -1;
    private Window? _gridFullscreenWindow;

    public LiveViewControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        Loaded += (_, _) =>
        {
            _hookProc = MouseHookCallback;
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(null), 0);
        };

        Unloaded += (_, _) =>
        {
            if (_gridFullscreenWindow != null)
            {
                _gridFullscreenWindow.Close();
            }

            if (_mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _gridFullscreenWindow != null)
            {
                _gridFullscreenWindow.Close();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && DataContext is LiveViewViewModel vm && vm.IsZoomedIn)
            {
                vm.ExitZoomCommand?.Execute(null);
                e.Handled = true;
            }
        };
    }

    private IntPtr MouseHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        var next = CallNextHookEx(_mouseHook, code, wParam, lParam);

        if (code >= 0 && (int)wParam == WM_LBUTTONDOWN)
        {
            try
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var elapsed = unchecked(info.time - _lastClickTick);
                var dx = Math.Abs(info.pt.X - _lastClickX);
                var dy = Math.Abs(info.pt.Y - _lastClickY);

                if (elapsed <= GetDoubleClickTime()
                    && dx <= GetSystemMetrics(SM_CXDOUBLECLK)
                    && dy <= GetSystemMetrics(SM_CYDOUBLECLK)
                    && IsLoaded
                    && IsVisible
                    && DataContext is LiveViewViewModel vm
                    && !vm.IsZoomedIn)
                {
                    var localPt = TilesItemsControl.PointFromScreen(new Point(info.pt.X, info.pt.Y));
                    var tile = FindTileAtPoint(localPt);
                    if (tile != null && !string.IsNullOrWhiteSpace(tile.CameraId))
                    {
                        vm.BeginZoomTile(tile);
                    }

                    _lastClickTick = 0;
                    _lastClickX = 0;
                    _lastClickY = 0;
                }
                else
                {
                    _lastClickTick = info.time;
                    _lastClickX = info.pt.X;
                    _lastClickY = info.pt.Y;
                }
            }
            catch
            {
            }
        }

        return next;
    }

    private VideoTileViewModel? FindTileAtPoint(Point pt)
    {
        for (var i = 0; i < TilesItemsControl.Items.Count; i++)
        {
            if (TilesItemsControl.Items[i] is not VideoTileViewModel tileVm) continue;
            if (TilesItemsControl.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container) continue;
            var topLeft = container.TranslatePoint(new Point(0, 0), TilesItemsControl);
            if (new Rect(topLeft.X, topLeft.Y, container.ActualWidth, container.ActualHeight).Contains(pt))
            {
                return tileVm;
            }
        }

        return null;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is LiveViewViewModel oldViewModel) oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is LiveViewViewModel newViewModel) newViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
    }

    private async void FullscreenButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_gridFullscreenWindow != null)
        {
            _gridFullscreenWindow.Close();
            return;
        }

        if (DataContext is LiveViewViewModel vm && vm.IsZoomedIn)
        {
            await vm.ExitZoomAsync();
        }

        EnterGridFullscreen();
    }

    private void EnterGridFullscreen()
    {
        if (TilesItemsControl.Parent is not Panel originalParent)
        {
            return;
        }

        _tilesOriginalParent = originalParent;
        _tilesOriginalIndex = originalParent.Children.IndexOf(TilesItemsControl);
        originalParent.Children.Remove(TilesItemsControl);
        TilesItemsControl.DataContext = DataContext;

        var fullscreenHost = new Grid
        {
            Background = (Brush?)TryFindResource("SurfaceBrush") ?? Brushes.Black
        };
        fullscreenHost.Children.Add(TilesItemsControl);

        var monitorBounds = GetCurrentMonitorBounds();
        var fullscreenWindow = new Window
        {
            Title = "Grid View",
            Content = fullscreenHost,
            Background = (Brush?)TryFindResource("SurfaceBrush") ?? Brushes.Black,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = monitorBounds.Left,
            Top = monitorBounds.Top,
            Width = monitorBounds.Width,
            Height = monitorBounds.Height,
            Topmost = true
        };

        fullscreenWindow.PreviewKeyDown += FullscreenWindow_OnPreviewKeyDown;
        fullscreenWindow.Closed += (_, _) => RestoreGridViewHost();
        _gridFullscreenWindow = fullscreenWindow;
        fullscreenWindow.Show();
        fullscreenWindow.Activate();
    }

    private Rect GetCurrentMonitorBounds()
    {
        var hostWindow = Window.GetWindow(this);
        if (hostWindow == null)
        {
            return new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        }

        var handle = new WindowInteropHelper(hostWindow).Handle;
        if (handle == IntPtr.Zero)
        {
            return new Rect(hostWindow.Left, hostWindow.Top, hostWindow.ActualWidth, hostWindow.ActualHeight);
        }

        var monitor = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return new Rect(hostWindow.Left, hostWindow.Top, hostWindow.ActualWidth, hostWindow.ActualHeight);
        }

        return new Rect(
            info.rcMonitor.Left,
            info.rcMonitor.Top,
            info.rcMonitor.Right - info.rcMonitor.Left,
            info.rcMonitor.Bottom - info.rcMonitor.Top);
    }
    private void FullscreenWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || e.Key == Key.F11)
        {
            _gridFullscreenWindow?.Close();
            e.Handled = true;
        }
    }

    private void RestoreGridViewHost()
    {
        if (_gridFullscreenWindow != null)
        {
            _gridFullscreenWindow.PreviewKeyDown -= FullscreenWindow_OnPreviewKeyDown;
            _gridFullscreenWindow = null;
        }

        if (_tilesOriginalParent == null)
        {
            return;
        }

        if (TilesItemsControl.Parent is Panel fullscreenHost)
        {
            fullscreenHost.Children.Remove(TilesItemsControl);
        }

        TilesItemsControl.ClearValue(DataContextProperty);

        if (_tilesOriginalIndex >= 0 && _tilesOriginalIndex <= _tilesOriginalParent.Children.Count)
        {
            _tilesOriginalParent.Children.Insert(_tilesOriginalIndex, TilesItemsControl);
        }
        else
        {
            _tilesOriginalParent.Children.Add(TilesItemsControl);
        }

        _tilesOriginalParent = null;
        _tilesOriginalIndex = -1;
    }

    private void Tile_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        e.Handled = true;
        if (DataContext is not LiveViewViewModel vm) return;
        if (sender is not Border { DataContext: VideoTileViewModel tile }) return;
        if (!string.IsNullOrWhiteSpace(tile.CameraId)) vm.BeginZoomTile(tile);
    }

    private void Tile_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        e.Handled = true;
        if (DataContext is not LiveViewViewModel vm) return;
        if (sender is not Border { DataContext: VideoTileViewModel tile }) return;
        if (!string.IsNullOrWhiteSpace(tile.CameraId)) vm.BeginZoomTile(tile);
    }

    private void Tile_OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not LiveViewViewModel vm) return;
        var cameraId = e.Data.GetData(typeof(string)) as string;
        if (string.IsNullOrWhiteSpace(cameraId)) return;
        if (sender is not Border border || border.Child is not Grid grid || grid.Tag is not string tileId) return;
        vm.BeginAssignCameraToTile(tileId, cameraId);
        e.Handled = true;
    }
}