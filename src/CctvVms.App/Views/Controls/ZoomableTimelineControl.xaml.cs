using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CctvVms.App.Views.Controls
{
    public partial class ZoomableTimelineControl : UserControl
    {
        public static readonly DependencyProperty PositionProperty =
            DependencyProperty.Register(nameof(Position), typeof(double), typeof(ZoomableTimelineControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPositionChanged));

        public static readonly DependencyProperty SeekCommandProperty =
            DependencyProperty.Register(nameof(SeekCommand), typeof(ICommand), typeof(ZoomableTimelineControl));

        public double Position
        {
            get => (double)GetValue(PositionProperty);
            set => SetValue(PositionProperty, Math.Clamp(value, 0, 86400));
        }

        public ICommand? SeekCommand
        {
            get => (ICommand?)GetValue(SeekCommandProperty);
            set => SetValue(SeekCommandProperty, value);
        }

        private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((ZoomableTimelineControl)d).Redraw();

        // Visible window in seconds
        private double _viewStart = 0;
        private double _viewEnd = 86400;

        // Drag state
        private bool _isDragging;
        private double _dragStartX;
        private double _dragStartViewStart;
        private double _dragStartViewEnd;

        private double ViewRange => _viewEnd - _viewStart;

        public ZoomableTimelineControl()
        {
            InitializeComponent();
            Loaded += (_, _) => Redraw();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            Redraw();
        }

        private double SecondsToX(double s)
        {
            var w = TimelineCanvas.ActualWidth;
            return w <= 0 ? 0 : (s - _viewStart) / ViewRange * w;
        }

        private double XToSeconds(double x)
        {
            var w = TimelineCanvas.ActualWidth;
            return w <= 0 ? 0 : _viewStart + x / w * ViewRange;
        }

        private void Redraw()
        {
            if (TimelineCanvas == null) return;
            TimelineCanvas.Children.Clear();

            var w = TimelineCanvas.ActualWidth;
            var h = TimelineCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            var trackBrush    = new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x50));
            var majorBrush    = new SolidColorBrush(Color.FromRgb(0x55, 0x70, 0x90));
            var minorBrush    = new SolidColorBrush(Color.FromRgb(0x2E, 0x40, 0x58));
            var labelBrush    = new SolidColorBrush(Color.FromRgb(0x8F, 0xA8, 0xC0));
            var accentBrush   = new SolidColorBrush(Color.FromRgb(0x12, 0xB8, 0x86));
            var progressBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x12, 0xB8, 0x86));

            double trackY = h * 0.72;

            // Track background
            TimelineCanvas.Children.Add(new Rectangle
            {
                Width = w, Height = 3,
                Fill = trackBrush,
                RadiusX = 1.5, RadiusY = 1.5
            });
            Canvas.SetLeft(TimelineCanvas.Children[^1], 0);
            Canvas.SetTop(TimelineCanvas.Children[^1], trackY);

            // Progress fill (from 0 to position)
            double posX = SecondsToX(Position);
            if (posX > 0)
            {
                TimelineCanvas.Children.Add(new Rectangle
                {
                    Width = Math.Max(0, posX), Height = 3,
                    Fill = accentBrush,
                    RadiusX = 1.5, RadiusY = 1.5
                });
                Canvas.SetLeft(TimelineCanvas.Children[^1], 0);
                Canvas.SetTop(TimelineCanvas.Children[^1], trackY);
            }

            // Tick intervals based on visible range
            double range = ViewRange;
            double majorInterval, minorInterval;
            string fmt;

            if      (range > 3600 * 8)   { majorInterval = 7200;  minorInterval = 3600;  fmt = "HH:mm"; }
            else if (range > 3600 * 3)   { majorInterval = 3600;  minorInterval = 1800;  fmt = "HH:mm"; }
            else if (range > 3600 * 1.5) { majorInterval = 1800;  minorInterval = 600;   fmt = "HH:mm"; }
            else if (range > 3600 * 0.5) { majorInterval = 600;   minorInterval = 120;   fmt = "HH:mm"; }
            else if (range > 600)        { majorInterval = 300;   minorInterval = 60;    fmt = "HH:mm"; }
            else if (range > 120)        { majorInterval = 60;    minorInterval = 30;    fmt = "HH:mm:ss"; }
            else                         { majorInterval = 30;    minorInterval = 10;    fmt = "HH:mm:ss"; }

            // Minor ticks
            double firstMinor = Math.Ceiling(_viewStart / minorInterval) * minorInterval;
            for (double s = firstMinor; s <= _viewEnd + 0.001; s += minorInterval)
            {
                double x = SecondsToX(s);
                TimelineCanvas.Children.Add(new Line
                {
                    X1 = x, Y1 = trackY - 4, X2 = x, Y2 = trackY + 2,
                    Stroke = minorBrush, StrokeThickness = 1
                });
            }

            // Major ticks + labels
            double firstMajor = Math.Ceiling(_viewStart / majorInterval) * majorInterval;
            for (double s = firstMajor; s <= _viewEnd + 0.001; s += majorInterval)
            {
                double x = SecondsToX(s);
                TimelineCanvas.Children.Add(new Line
                {
                    X1 = x, Y1 = trackY - 9, X2 = x, Y2 = trackY + 2,
                    Stroke = majorBrush, StrokeThickness = 1.5
                });

                var dt = DateTime.Today.AddSeconds(s);
                var tb = new TextBlock
                {
                    Text = dt.ToString(fmt),
                    Foreground = labelBrush,
                    FontSize = 9,
                    FontFamily = new FontFamily("Consolas, Segoe UI")
                };
                tb.Measure(new Size(80, 16));
                double lx = x - tb.DesiredSize.Width / 2;
                Canvas.SetLeft(tb, Math.Clamp(lx, 0, w - tb.DesiredSize.Width));
                Canvas.SetTop(tb, 1);
                TimelineCanvas.Children.Add(tb);
            }

            // Playhead line
            if (posX >= 0 && posX <= w)
            {
                TimelineCanvas.Children.Add(new Line
                {
                    X1 = posX, Y1 = 0, X2 = posX, Y2 = h,
                    Stroke = accentBrush, StrokeThickness = 2
                });

                // Diamond/triangle marker at top
                TimelineCanvas.Children.Add(new Polygon
                {
                    Fill = accentBrush,
                    Points = new PointCollection
                    {
                        new Point(posX - 4, trackY - 10),
                        new Point(posX + 4, trackY - 10),
                        new Point(posX, trackY - 4)
                    }
                });
            }
        }

        // -- Mouse wheel: zoom in/out around cursor --------------------------
        private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double mouseX  = e.GetPosition(TimelineCanvas).X;
            double mouseSec = XToSeconds(mouseX);
            double factor   = e.Delta > 0 ? 0.6 : 1.6;
            double newRange = Math.Clamp(ViewRange * factor, 60, 86400);
            double ratio    = (mouseSec - _viewStart) / ViewRange;

            _viewStart = mouseSec - ratio * newRange;
            _viewEnd   = _viewStart + newRange;

            if (_viewStart < 0)     { _viewEnd   -= _viewStart;           _viewStart = 0;     }
            if (_viewEnd > 86400)   { _viewStart -= (_viewEnd - 86400);   _viewEnd   = 86400; }
            _viewStart = Math.Max(0, _viewStart);
            _viewEnd   = Math.Min(86400, _viewEnd);

            Redraw();
            e.Handled = true;
        }

        // -- Mouse drag: pan timeline -----------------------------------------
        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging       = false;
            _dragStartX       = e.GetPosition(TimelineCanvas).X;
            _dragStartViewStart = _viewStart;
            _dragStartViewEnd   = _viewEnd;
            TimelineCanvas.CaptureMouse();
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!TimelineCanvas.IsMouseCaptured) return;
            double dx    = e.GetPosition(TimelineCanvas).X - _dragStartX;
            double dSecs = dx / TimelineCanvas.ActualWidth * (_dragStartViewEnd - _dragStartViewStart);

            if (Math.Abs(dx) > 4) _isDragging = true;

            if (_isDragging)
            {
                double ns = _dragStartViewStart - dSecs;
                double ne = _dragStartViewEnd   - dSecs;
                if (ns < 0)     { ne -= ns;           ns = 0;     }
                if (ne > 86400) { ns -= (ne - 86400); ne = 86400; }
                _viewStart = Math.Max(0, ns);
                _viewEnd   = Math.Min(86400, ne);
                Redraw();
            }
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            TimelineCanvas.ReleaseMouseCapture();
            if (!_isDragging)
            {
                double secs = XToSeconds(e.GetPosition(TimelineCanvas).X);
                Position = Math.Clamp(secs, 0, 86400);
                if (SeekCommand?.CanExecute(null) == true)
                    SeekCommand.Execute(null);
            }
            _isDragging = false;
        }
    }
}
