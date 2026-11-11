using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace BtManage.Views;

/// <summary>A ten-segment charge meter.</summary>
public sealed partial class BatteryMeter : UserControl
{
    private const int Segments = 10;
    private const double Gap = 2;

    private int _filled;

    public BatteryMeter()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Draw();
    }

    /// <summary>Sets the level. A null <paramref name="percent"/> leaves every segment empty.</summary>
    public void Set(int? percent, bool low)
    {
        _filled = percent is { } value ? Math.Clamp((int)Math.Round(value / 10.0), 0, Segments) : 0;
        VisualStateManager.GoToState(this, low ? "Low" : "Normal", useTransitions: false);
        Draw();
    }

    private void Draw()
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        // Whole pixels per segment, so every segment and every gap is the same size.
        var segment = Math.Floor((width - Gap * (Segments - 1)) / Segments);
        var track = new GeometryGroup();
        var filled = new GeometryGroup();
        for (var i = 0; i < Segments; i++)
        {
            var rect = new RectangleGeometry { Rect = new Rect(i * (segment + Gap), 0, segment, height) };
            (i < _filled ? filled : track).Children.Add(rect);
        }

        Track.Data = track;
        Filled.Data = filled;
    }
}
