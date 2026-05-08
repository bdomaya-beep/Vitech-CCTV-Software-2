using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string? s);

    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int SM_CXDOUBLECLK = 36;
    private const int SM_CYDOUBLECLK = 37;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData, flags, time;
        public IntPtr dwExtraInfo;
    }

    // Must be instance field — prevents delegate from being GC'd while hook is active.
    private HookProc? _hookProc;
    private IntPtr _mouseHook = IntPtr.Zero;
    private uint _lastClickTick;
    private int _lastClickX, _lastClickY;

    public LiveViewControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        Loaded += (_, _) =>
        {
            _hookProc = MouseHookCallback;
            // threadId = 0 → system-wide hook (required for WH_MOUSE_LL).
            // Callback fires on THIS thread via its message loop — safe to touch WPF elements.
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(null), 0);
        };

        Unloaded += (_, _) =>
        {
            if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        };

        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape && DataContext is LiveViewViewModel vm && vm.IsZoomedIn)
            {
                vm.ExitZoomCommand?.Execute(null);
                e.Handled = true;
            }
        };
    }

    private IntPtr MouseHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        // Always call next hook first — we NEVER suppress mouse events.
        var next = CallNextHookEx(_mouseHook, code, wParam, lParam);

        if (code >= 0 && (int)wParam == WM_LBUTTONDOWN)
        {
            try
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                // Manual double-click detection: WH_MOUSE_LL never receives WM_LBUTTONDBLCLK.
                var elapsed = unchecked(info.time - _lastClickTick); // uint wrap-safe
                var dx = Math.Abs(info.pt.X - _lastClickX);
                var dy = Math.Abs(info.pt.Y - _lastClickY);

                if (elapsed <= GetDoubleClickTime()
                    && dx <= GetSystemMetrics(SM_CXDOUBLECLK)
                    && dy <= GetSystemMetrics(SM_CYDOUBLECLK)
                    && IsLoaded && IsVisible
                    && DataContext is LiveViewViewModel vm && !vm.IsZoomedIn)
                {
                    // Convert screen coords to TilesItemsControl-local logical coords.
                    var localPt = TilesItemsControl.PointFromScreen(new Point(info.pt.X, info.pt.Y));
                    var tile = FindTileAtPoint(localPt);
                    if (tile != null && !string.IsNullOrWhiteSpace(tile.CameraId))
                        vm.BeginZoomTile(tile);

                    // Reset so a third click doesn't re-trigger.
                    _lastClickTick = 0;
                    _lastClickX = _lastClickY = 0;
                }
                else
                {
                    _lastClickTick = info.time;
                    _lastClickX = info.pt.X;
                    _lastClickY = info.pt.Y;
                }
            }
            catch { /* Never throw inside a hook callback. */ }
        }

        return next;
    }

    private VideoTileViewModel? FindTileAtPoint(Point pt)
    {
        for (var i = 0; i < TilesItemsControl.Items.Count; i++)
        {
            if (TilesItemsControl.Items[i] is not VideoTileViewModel tileVm) continue;
            if (TilesItemsControl.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
            var topLeft = c.TranslatePoint(new Point(0, 0), TilesItemsControl);
            if (new Rect(topLeft.X, topLeft.Y, c.ActualWidth, c.ActualHeight).Contains(pt))
                return tileVm;
        }
        return null;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is LiveViewViewModel o) o.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is LiveViewViewModel n) n.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) { }

    // Fallback for empty tiles (no HwndHost, WPF events work normally).
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
