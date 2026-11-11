using BtManage.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BtManage.Views;

/// <summary>A device with its batteries: three columns for earbuds, one for everything else.</summary>
public sealed partial class DeviceCard : UserControl
{
    public DeviceCard()
    {
        InitializeComponent();
    }

    /// <summary>Whether the name row is shown. The window shows the name in its own header.</summary>
    internal bool ShowName
    {
        get => Header.Visibility == Visibility.Visible;
        set => Header.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void Set(Device device, int lowThreshold)
    {
        NameText.Text = DeviceText.Name(device);
        StatusText.Text = device.Connected ? string.Empty : DeviceText.Status(device);

        if (device.HasEarbudCells)
        {
            First.Set(Role.Left, device.Cell(Role.Left), device.Kind, lowThreshold);
            Second.Set(Role.Right, device.Cell(Role.Right), device.Kind, lowThreshold);
            Third.Set(Role.Case, device.Cell(Role.Case), device.Kind, lowThreshold);
        }
        else
        {
            First.Set(Role.Main, device.Cell(Role.Main), device.Kind, lowThreshold);
        }

        // A single battery takes the whole width.
        Grid.SetColumnSpan(First, device.HasEarbudCells ? 1 : 3);
        var earbuds = device.HasEarbudCells ? Visibility.Visible : Visibility.Collapsed;
        Second.Visibility = earbuds;
        Third.Visibility = earbuds;
    }
}
