using BtManage.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BtManage.Views;

/// <summary>One battery: its outline, level, meter and label.</summary>
public sealed partial class BatteryCell : UserControl
{
    public BatteryCell()
    {
        InitializeComponent();
    }

    /// <param name="cell">Null for a level that has not been reported.</param>
    /// <param name="kind">Kind of the device, for the glyph of a single battery.</param>
    /// <param name="lowThreshold">At or below this level the meter turns to the warning colour.</param>
    internal void Set(Role role, Cell? cell, Kind kind, int lowThreshold)
    {
        LeftOutline.Visibility = Visible(role == Role.Left);
        RightOutline.Visibility = Visible(role == Role.Right);
        CaseOutline.Visibility = Visible(role == Role.Case);
        KindIcon.Visibility = Visible(role == Role.Main);
        KindIcon.Glyph = DeviceText.Glyph(kind);

        // No level shows as a dash; a remembered one is dimmed and carries its age.
        var current = cell is { IsFresh: true };
        PercentText.Text = cell is null ? "—" : DeviceText.Percent(cell.Percent);
        PercentText.Style = Appearance.Style(
            cell is null ? "BtmTertiaryTextStyle" : current ? "BtmPrimaryTextStyle" : "BtmSecondaryTextStyle");
        Meter.Set(cell?.Percent, low: cell?.IsLow(lowThreshold) == true, dimmed: !current);

        var label = DeviceText.RoleLabel(role);
        var note = cell switch
        {
            null => null,
            { IsFresh: false } => DeviceText.Age(cell.AgeMs),
            { InEar: true } => Strings.Get("InEar"),
            _ => null,
        };
        LabelText.Text = note is null ? label : $"{label} · {note}";
        ChargingIcon.Visibility = Visible(cell is { Charging: true, IsFresh: true });

        var spoken = cell is null ? label : $"{label}, {DeviceText.Percent(cell.Percent)}";
        if (cell is { Charging: true })
        {
            spoken += $", {Strings.Get("Charging")}";
        }

        AutomationProperties.SetName(this, note is null ? spoken : $"{spoken}, {note}");
    }

    private static Visibility Visible(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
