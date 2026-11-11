using BtManage.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace BtManage.Views;

/// <summary>
/// A device as one row of a list: the glyph of its kind, its name and its level. The row
/// carries the device identifier in <see cref="FrameworkElement.Tag"/>.
/// </summary>
internal static class DeviceRow
{
    /// <param name="withStatus">Adds a second line with the connection state.</param>
    public static Grid Create(Device device, int lowThreshold, bool withStatus)
    {
        var row = new Grid { ColumnSpacing = 12, MinHeight = withStatus ? 52 : 40, Tag = device.Id };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        row.Children.Add(new FontIcon
        {
            Glyph = DeviceText.Glyph(device.Kind),
            FontSize = 16,
            Width = 20,
            Foreground = Brush("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var name = new TextBlock
        {
            Text = DeviceText.Name(device),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (withStatus)
        {
            var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            lines.Children.Add(name);
            lines.Children.Add(Caption(DeviceText.Status(device)));
            Grid.SetColumn(lines, 1);
            row.Children.Add(lines);
        }
        else
        {
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
        }

        var level = Level(device, lowThreshold, statusWhenUnknown: !withStatus);
        Grid.SetColumn(level, 2);
        row.Children.Add(level);

        string[] spoken = [DeviceText.Name(device), DeviceText.Status(device), DeviceText.Levels(device)];
        AutomationProperties.SetName(row, string.Join(", ", spoken.Where(part => part.Length > 0)));
        return row;
    }

    /// <summary>What a row shows, as text. Two rows with the same text look the same.</summary>
    public static string Signature(Device device, int lowThreshold)
    {
        var cell = device.LowestCell;
        return string.Join('|', device.Id, device.Kind, DeviceText.Name(device), DeviceText.Status(device),
            cell?.Percent, cell?.Charging, cell?.IsFresh, cell?.IsLow(lowThreshold));
    }

    /// <summary>The level on the right of a row: the lowest battery, the case aside.</summary>
    private static FrameworkElement Level(Device device, int lowThreshold, bool statusWhenUnknown)
    {
        if (device.LowestCell is not { } cell)
        {
            return statusWhenUnknown ? Caption(DeviceText.Status(device)) : new Grid();
        }

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (cell is { Charging: true, IsFresh: true })
        {
            panel.Children.Add(new FontIcon
            {
                Glyph = "",
                FontSize = 12,
                Foreground = Brush("TextFillColorSecondaryBrush"),
            });
        }

        var text = new TextBlock
        {
            Text = DeviceText.Percent(cell.Percent),
            Foreground = Brush(
                !cell.IsFresh ? "TextFillColorTertiaryBrush"
                : cell.IsLow(lowThreshold) ? "SystemFillColorCriticalBrush"
                : "TextFillColorPrimaryBrush"),
        };
        Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
        panel.Children.Add(text);
        return panel;
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Foreground = Brush("TextFillColorSecondaryBrush"),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
