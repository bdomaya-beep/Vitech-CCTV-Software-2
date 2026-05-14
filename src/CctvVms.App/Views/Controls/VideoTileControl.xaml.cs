using System.Windows;
using System.Windows.Controls;
using CctvVms.Core.Streaming;

namespace CctvVms.App.Views.Controls;

public partial class VideoTileControl : UserControl
{
    public static readonly DependencyProperty VideoSourceProperty =
        DependencyProperty.Register(nameof(VideoSource), typeof(IVideoSource),
            typeof(VideoTileControl), new PropertyMetadata(null, OnSourceChanged));

    public IVideoSource? VideoSource
    {
        get => (IVideoSource?)GetValue(VideoSourceProperty);
        set => SetValue(VideoSourceProperty, value);
    }

    public VideoTileControl() => InitializeComponent();

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (VideoTileControl)d;
        ctrl._gl.AttachSource(e.NewValue as IVideoSource);
    }
}
