using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CctvVms.App.Views.Controls
{
    public partial class DarkDatePicker : UserControl
    {
        public static readonly DependencyProperty SelectedDateProperty =
            DependencyProperty.Register(nameof(SelectedDate), typeof(DateTime), typeof(DarkDatePicker),
                new FrameworkPropertyMetadata(DateTime.Today, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedDateChanged));

        public DateTime SelectedDate
        {
            get => (DateTime)GetValue(SelectedDateProperty);
            set => SetValue(SelectedDateProperty, value);
        }

        private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (DarkDatePicker)d;
            ctrl._viewMonth = ((DateTime)e.NewValue);
            ctrl.UpdateDisplay();
            ctrl.BuildGrid();
        }

        private DateTime _viewMonth;

        public DarkDatePicker()
        {
            InitializeComponent();
            _viewMonth = SelectedDate;
            UpdateDisplay();
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _viewMonth = SelectedDate == default ? DateTime.Today : SelectedDate;
            BuildGrid();
            CalendarPopup.IsOpen = true;
        }

        private void CalendarPopup_Closed(object sender, EventArgs e) { }

        private void PrevMonth_Click(object sender, RoutedEventArgs e)
        {
            _viewMonth = _viewMonth.AddMonths(-1);
            BuildGrid();
        }

        private void NextMonth_Click(object sender, RoutedEventArgs e)
        {
            _viewMonth = _viewMonth.AddMonths(1);
            BuildGrid();
        }

        private void UpdateDisplay()
        {
            if (DateDisplay == null) return;
            DateDisplay.Text = SelectedDate == default ? "Select date..." : SelectedDate.ToString("MMM dd, yyyy");
        }

        private void BuildGrid()
        {
            MonthYearLabel.Text = _viewMonth.ToString("MMMM yyyy");

            // Day-of-week headers
            DayHeaders.Children.Clear();
            string[] dayNames = { "Su", "Mo", "Tu", "We", "Th", "Fr", "Sa" };
            foreach (var d in dayNames)
            {
                DayHeaders.Children.Add(new TextBlock
                {
                    Text = d,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9B, 0xB1, 0xCC)),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 4)
                });
            }

            // Day buttons
            DayGrid.Children.Clear();
            var firstDay = new DateTime(_viewMonth.Year, _viewMonth.Month, 1);
            int startOffset = (int)firstDay.DayOfWeek;
            int daysInMonth = DateTime.DaysInMonth(_viewMonth.Year, _viewMonth.Month);
            var today = DateTime.Today;
            var selected = SelectedDate.Date;

            // Fill leading blanks
            for (int i = 0; i < startOffset; i++)
                DayGrid.Children.Add(new TextBlock());

            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(_viewMonth.Year, _viewMonth.Month, day);
                bool isSelected = date == selected;
                bool isToday = date == today;

                var bg = isSelected
                    ? new SolidColorBrush(Color.FromRgb(0x12, 0xB8, 0x86))
                    : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

                var fg = isSelected
                    ? new SolidColorBrush(Color.FromRgb(0x06, 0x10, 0x18))
                    : new SolidColorBrush(Color.FromRgb(0xE9, 0xF1, 0xFF));

                var btn = new Button
                {
                    Content = day.ToString(),
                    Background = bg,
                    Foreground = fg,
                    BorderThickness = new Thickness(isToday ? 1 : 0),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x12, 0xB8, 0x86)),
                    FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = date,
                    Padding = new Thickness(0),
                    Margin = new Thickness(1),
                    MinWidth = 0,
                    MinHeight = 0,
                    Height = 28
                };

                btn.Click += DayButton_Click;

                // Hover style via event
                btn.MouseEnter += (s, _) =>
                {
                    var b = (Button)s;
                    var tagDate = (DateTime)b.Tag;
                    if (tagDate.Date != SelectedDate.Date)
                        b.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x2F, 0x4A));
                };
                btn.MouseLeave += (s, _) =>
                {
                    var b = (Button)s;
                    var tagDate = (DateTime)b.Tag;
                    b.Background = tagDate.Date == SelectedDate.Date
                        ? new SolidColorBrush(Color.FromRgb(0x12, 0xB8, 0x86))
                        : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                };

                DayGrid.Children.Add(btn);
            }
        }

        private void DayButton_Click(object sender, RoutedEventArgs e)
        {
            var date = (DateTime)((Button)sender).Tag;
            SelectedDate = date;
            CalendarPopup.IsOpen = false;
        }
    }
}
