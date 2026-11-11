using BtManage.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BtManage.Views;

/// <summary>The nearest device: its name and its batteries.</summary>
public sealed partial class DeviceCard : UserControl
{
    public DeviceCard()
    {
        InitializeComponent();
    }

    internal void Set(Device device)
    {
        NameText.Text = DeviceText.Name(device);
        AgeText.Text = DeviceText.Age(device);

        if (device.IsHeadphones)
        {
            First.Set(CellKind.Main, device.Main);
        }
        else
        {
            First.Set(CellKind.Left, device.Left);
            Second.Set(CellKind.Right, device.Right);
            Third.Set(CellKind.Case, device.Case);
        }

        var earbuds = device.IsHeadphones ? Visibility.Collapsed : Visibility.Visible;
        Second.Visibility = earbuds;
        Third.Visibility = earbuds;
    }
}
