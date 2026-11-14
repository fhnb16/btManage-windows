using BtManage.Bluetooth;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Devices.Enumeration;

namespace BtManage;

/// <summary>
/// Adding a device from the window: a dialog that lists what is ready to pair nearby, as
/// Windows Settings does, and pairs the one chosen. A code the device shows is put to the
/// user to compare.
/// </summary>
public sealed partial class MainWindow
{
    private async Task AddDeviceAsync()
    {
        var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        using var search = new PairingSearch(queue);
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MinHeight = 240, MaxHeight = 360, MinWidth = 360 };
        var hint = new TextBlock
        {
            Text = Strings.Get("AddDeviceHint"),
            TextWrapping = TextWrapping.Wrap,
            Style = Appearance.Style("BtmSecondaryTextStyle"),
        };
        var searching = new ProgressBar { IsIndeterminate = true };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(hint);
        panel.Children.Add(searching);
        panel.Children.Add(list);

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            RequestedTheme = Root.ActualTheme,
            Title = Strings.Get("AddDevice"),
            Content = panel,
            PrimaryButtonText = Strings.Get("AddDevicePair"),
            CloseButtonText = Strings.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };

        // The list follows what the search finds; the choice survives a refresh.
        search.Changed += () =>
        {
            var chosen = (list.SelectedItem as ListViewItem)?.Tag as string;
            list.Items.Clear();
            foreach (var info in search.Found)
            {
                var item = new ListViewItem { Content = info.Name, Tag = info.Id };
                list.Items.Add(item);
                if (info.Id == chosen)
                {
                    list.SelectedItem = item;
                }
            }
        };
        list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedItem is not null;
        search.Start();

        if (await dialog.ShowAsync() != ContentDialogResult.Primary
            || (list.SelectedItem as ListViewItem)?.Tag is not string id
            || search.Found.FirstOrDefault(info => info.Id == id) is not { } device)
        {
            return;
        }

        var status = await PairingSearch.PairAsync(device, ConfirmCodeAsync);
        var paired = status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired;
        await new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            RequestedTheme = Root.ActualTheme,
            Title = paired ? Strings.Format("AddDeviceDone", device.Name) : Strings.Format("AddDeviceFailed", device.Name),
            Content = paired ? null : new TextBlock { Text = Strings.Format("AddDeviceFailedHint", status), TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "OK",
        }.ShowAsync();
    }

    /// <summary>Asks whether the code the device shows is the one shown here.</summary>
    private async Task<bool> ConfirmCodeAsync(string code)
    {
        var answer = await new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            RequestedTheme = Root.ActualTheme,
            Title = Strings.Get("AddDeviceCode"),
            Content = new TextBlock { Text = Strings.Format("AddDeviceCodeHint", code), TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = Strings.Get("AddDeviceCodeMatches"),
            CloseButtonText = Strings.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        }.ShowAsync();
        return answer == ContentDialogResult.Primary;
    }
}
