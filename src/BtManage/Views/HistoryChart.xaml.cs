using BtManage.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace BtManage.Views;

/// <summary>
/// Battery level over the last 24 hours, drawn as steps: a level holds until the device
/// reports another. The line breaks where the device was away. One battery is drawn in the
/// accent colour, the others in neutral tones.
/// </summary>
public sealed partial class HistoryChart : UserControl
{
    internal static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>A longer silence than this breaks the line.</summary>
    private const long GapMs = 40 * 60_000;

    /// <summary>How far a level is drawn past its last report when nothing follows.</summary>
    private const long TailMs = 15 * 60_000;

    private const double LabelWidth = 32;
    private const double AxisHeight = 20;
    private const double TopInset = 6;

    private IReadOnlyList<RoleHistory> _series = [];
    private bool _active;

    public HistoryChart()
    {
        InitializeComponent();
        EmptyText.Text = Strings.Get("HistoryEmpty");
        Plot.SizeChanged += (_, _) => Draw();
        ActualThemeChanged += (_, _) => Draw();
    }

    /// <param name="active">The device is here now, so its last level holds up to this moment.</param>
    internal void Set(IReadOnlyList<RoleHistory> series, bool active)
    {
        _series = series;
        _active = active;
        Draw();
    }

    private void Draw()
    {
        Plot.Children.Clear();
        Legend.Children.Clear();

        var width = Plot.ActualWidth - LabelWidth;
        var height = Plot.ActualHeight - AxisHeight - TopInset;
        var empty = _series.Count == 0;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty || width <= 0 || height <= 0)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var end = now.ToUnixTimeMilliseconds();
        var start = end - (long)Window.TotalMilliseconds;
        double X(long t) => LabelWidth + Math.Clamp((t - start) / Window.TotalMilliseconds, 0, 1) * width;
        double Y(int percent) => TopInset + (100 - percent) / 100.0 * height;

        // Grid: three hairlines with their values, and the time under the ends and the middle.
        foreach (var percent in (int[])[0, 50, 100])
        {
            Plot.Children.Add(new Line
            {
                X1 = LabelWidth, X2 = LabelWidth + width, Y1 = Y(percent), Y2 = Y(percent),
                Stroke = Brush("DividerStrokeColorDefaultBrush"),
                StrokeThickness = 1,
            });
            Place(Label(percent.ToString()), 0, Y(percent) - 8, LabelWidth - 8, TextAlignment.Right);
        }

        var axis = TopInset + height + 4;
        Place(Label(now.Add(-Window).ToString("t")), LabelWidth, axis, 80, TextAlignment.Left);
        Place(Label(now.Add(-Window / 2).ToString("t")), LabelWidth + width / 2 - 40, axis, 80, TextAlignment.Center);
        Place(Label(Strings.Get("ChartNow")), LabelWidth + width - 80, axis, 80, TextAlignment.Right);

        // The leading line goes on top: draw it last.
        var lead = _series.Select(series => series.Role).Min();
        foreach (var series in _series.OrderByDescending(series => series.Role))
        {
            var brush = Brush(series.Role == lead ? "AccentFillColorDefaultBrush"
                : series.Role == Role.Case ? "TextFillColorTertiaryBrush"
                : "TextFillColorSecondaryBrush");
            foreach (var run in Runs(series.Points, start, end))
            {
                var line = new Polyline
                {
                    Stroke = brush,
                    StrokeThickness = series.Role == lead ? 2 : 1.5,
                    StrokeLineJoin = PenLineJoin.Round,
                };
                foreach (var (t, percent) in run)
                {
                    line.Points.Add(new Point(X(t), Y(percent)));
                }

                Plot.Children.Add(line);
            }

            if (_series.Count > 1)
            {
                Legend.Children.Insert(0, LegendItem(series.Role, brush));
            }
        }
    }

    /// <summary>
    /// Splits a series into unbroken runs of step corners. A level holds until the next
    /// report, or for a short tail when the next report is long in coming.
    /// </summary>
    private IEnumerable<List<(long T, int Percent)>> Runs(IReadOnlyList<HistoryPoint> points, long start, long end)
    {
        var run = new List<(long, int)>();
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var next = i + 1 < points.Count ? points[i + 1] : null;
            run.Add((point.T, point.Percent));

            if (next is not null && next.T - point.T <= GapMs)
            {
                run.Add((next.T, point.Percent));
                continue;
            }

            // Nothing follows soon: hold the level up to now if the device is here, else briefly.
            var last = next is null;
            var holdsToNow = last && _active && end - point.T <= GapMs;
            run.Add((holdsToNow ? end : Math.Min(point.T + TailMs, end), point.Percent));
            if (run[^1].Item1 > start)
            {
                yield return run;
            }

            run = [];
        }
    }

    private StackPanel LegendItem(Role role, Brush brush)
    {
        var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        item.Children.Add(new Rectangle { Width = 12, Height = 2, Fill = brush, VerticalAlignment = VerticalAlignment.Center });
        item.Children.Add(Label(DeviceText.RoleLabel(role)));
        return item;
    }

    private void Place(TextBlock label, double x, double y, double width, TextAlignment alignment)
    {
        label.Width = width;
        label.TextAlignment = alignment;
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        Plot.Children.Add(label);
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Foreground = Brush("TextFillColorTertiaryBrush"),
    };

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
