using BtManage.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BtManage.Views;

internal enum CellKind
{
    Left,
    Right,
    Case,
    Main,
}

/// <summary>One battery: its outline, level, meter and label.</summary>
public sealed partial class BatteryCell : UserControl
{
    public BatteryCell()
    {
        InitializeComponent();
    }

    internal void Set(CellKind kind, Cell? cell)
    {
        LeftOutline.Visibility = Visible(kind == CellKind.Left);
        RightOutline.Visibility = Visible(kind == CellKind.Right);
        CaseOutline.Visibility = Visible(kind == CellKind.Case);
        MainOutline.Visibility = Visible(kind == CellKind.Main);

        var label = Strings.Get(kind switch
        {
            CellKind.Left => "CellLeft",
            CellKind.Right => "CellRight",
            CellKind.Case => "CellCase",
            _ => "CellMain",
        });

        // A level that has not been reported yet shows as a dash, in the weakest text colour.
        PercentText.Text = cell is null ? "—" : DeviceText.Percent(cell.Percent);
        PercentText.Foreground = (Brush)Application.Current.Resources[
            cell is null ? "TextFillColorTertiaryBrush" : "TextFillColorPrimaryBrush"];
        Meter.Set(cell?.Percent, low: cell is { IsLow: true });

        LabelText.Text = cell is { InEar: true } ? $"{label} · {Strings.Get("InEar")}" : label;
        ChargingIcon.Visibility = Visible(cell is { Charging: true });

        var spoken = cell is null ? label : $"{label}, {DeviceText.Percent(cell.Percent)}";
        if (cell is { Charging: true })
        {
            spoken += $", {Strings.Get("Charging")}";
        }

        AutomationProperties.SetName(this, spoken);
    }

    private static Visibility Visible(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
