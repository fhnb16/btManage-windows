using System.Diagnostics;
using BtManage.Interop;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Windows.ApplicationModel.DataTransfer;

namespace BtManage;

/// <summary>
/// The page of sharing with other devices that run btManage on the same network.
///
/// At the top, this computer: whether it takes items, and the code another device types in
/// to pair with it, large, since it is read out across a room. Then the devices heard, each
/// with what can be done with it: pair, or send text or a file. Last, what came and went,
/// with what can be done with it now.
/// </summary>
public sealed partial class MainWindow
{
    private TextBlock? _shareCode;
    private TextBlock? _shareStatus;
    private StackPanel? _sharePeers;
    private StackPanel? _shareHistory;
    private string _sharePeersShown = string.Empty;
    private string _shareHistoryShown = string.Empty;

    /// <summary>An address typed in by hand, shown among the devices heard.</summary>
    private string? _shareAddress;

    private void BuildSharePage()
    {
        SharePanel.Children.Add(new TextBlock
        {
            Text = Strings.Get("SharePage"),
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
            Margin = new Thickness(0, 0, 0, 4),
        });

        // This computer.
        var device = new StackPanel { Spacing = 12 };
        device.Children.Add(CardTitle("ShareThisComputer"));
        _shareStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Style = Appearance.Style("BtmSecondaryTextStyle") };
        device.Children.Add(_shareStatus);

        var receive = new Grid { ColumnSpacing = 16 };
        receive.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        receive.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        receive.Children.Add(new TextBlock { Text = Strings.Get("ShareReceive"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        var toggle = Switch(_settings.ShareReceive, on => Save(() => _settings.ShareReceive = on));
        Grid.SetColumn(toggle, 1);
        receive.Children.Add(toggle);
        device.Children.Add(receive);

        var code = new StackPanel { Spacing = 2 };
        code.Children.Add(new TextBlock { Text = Strings.Get("ShareCode"), Style = Appearance.Style("BtmSecondaryCaptionStyle") });
        _shareCode = new TextBlock { FontSize = 28, FontWeight = FontWeights.SemiBold, CharacterSpacing = 80 };
        Typography.SetNumeralAlignment(_shareCode, FontNumeralAlignment.Tabular);
        code.Children.Add(_shareCode);
        code.Children.Add(new TextBlock
        {
            Text = Strings.Get("ShareCodeHint"),
            TextWrapping = TextWrapping.Wrap,
            Style = Appearance.Style("BtmSecondaryCaptionStyle"),
        });
        device.Children.Add(code);

        var folder = new Button { Content = Strings.Get("ShareOpenFolder") };
        folder.Click += (_, _) =>
        {
            Directory.CreateDirectory(Sharing.Inbox);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Sharing.Inbox}\"") { UseShellExecute = true });
        };
        device.Children.Add(folder);
        SharePanel.Children.Add(Card(device));

        // The devices on the network.
        var peers = new StackPanel { Spacing = 12 };
        peers.Children.Add(CardTitle("SharePeers"));
        _sharePeers = new StackPanel { Spacing = 12 };
        peers.Children.Add(_sharePeers);
        var byAddress = new Button { Content = Strings.Get("ShareByAddress") };
        byAddress.Click += OnShareByAddress;
        peers.Children.Add(byAddress);
        SharePanel.Children.Add(Card(peers));

        // What came and went.
        var recent = new StackPanel { Spacing = 12 };
        recent.Children.Add(CardTitle("ShareHistory"));
        _shareHistory = new StackPanel { Spacing = 16 };
        recent.Children.Add(_shareHistory);
        SharePanel.Children.Add(Card(recent));

        _sharing.Changed += ShowSharePage;
    }

    private Border Card(UIElement content) => new()
    {
        Style = (Style)Root.Resources["Card"],
        Child = content,
    };

    private static TextBlock CardTitle(string key) => new()
    {
        Text = Strings.Get(key),
        Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
    };

    /// <summary>Brings the page up to date: the code, the devices, the history.</summary>
    private void ShowSharePage()
    {
        if (_shareCode is null || _shareStatus is null || _sharePeers is null || _shareHistory is null)
        {
            return;
        }

        if (!_sharing.Running)
        {
            _shareStatus.Text = Strings.Get("ShareNotRunning");
            _shareCode.Text = string.Empty;
        }
        else
        {
            var addresses = _sharing.Addresses();
            _shareStatus.Text = Strings.Format("ShareVisible", _sharing.Name, addresses.Count > 0 ? string.Join(", ", addresses) : "—");
            var digits = _sharing.Code;
            _shareCode.Text = digits.Length == 6 ? $"{digits[..3]} {digits[3..]}" : digits;
        }

        // The rows are rebuilt only when what they show changed, so that a press is not lost.
        var peers = _sharing.Peers;
        var peersShown = string.Join('\n', peers.Select(peer => $"{peer.Id}|{peer.Name}|{peer.Address}|{peer.Trusted}")) + "|" + _shareAddress;
        if (peersShown != _sharePeersShown)
        {
            _sharePeersShown = peersShown;
            _sharePeers.Children.Clear();
            if (_shareAddress is { } typed)
            {
                _sharePeers.Children.Add(PeerRow(typed, Strings.Get("ShareTypedAddress"), trusted: null, id: null));
            }

            foreach (var peer in peers)
            {
                var state = Strings.Get(peer.Trusted ? "SharePaired" : "ShareNotPaired");
                _sharePeers.Children.Add(PeerRow(peer.Name, $"{PlatformName(peer.Platform)} · {state}", peer.Trusted, peer.Id, peer.Address));
            }

            if (peers.Count == 0 && _shareAddress is null)
            {
                _sharePeers.Children.Add(new TextBlock
                {
                    Text = Strings.Get("ShareNoPeers"),
                    TextWrapping = TextWrapping.Wrap,
                    Style = Appearance.Style("BtmSecondaryTextStyle"),
                });
            }
        }

        var history = _sharing.History;
        var historyShown = $"{history.Count}|{history.FirstOrDefault()?.At:O}";
        if (historyShown != _shareHistoryShown)
        {
            _shareHistoryShown = historyShown;
            _shareHistory.Children.Clear();
            foreach (var item in history.Take(30))
            {
                _shareHistory.Children.Add(HistoryRow(item));
            }

            if (history.Count == 0)
            {
                _shareHistory.Children.Add(new TextBlock
                {
                    Text = Strings.Get("ShareNothingYet"),
                    Style = Appearance.Style("BtmSecondaryTextStyle"),
                });
            }
        }
    }

    private static string PlatformName(string platform) => platform switch
    {
        "android" => "Android",
        "windows" => "Windows",
        "linux" => "Linux",
        _ => platform,
    };

    /// <summary>
    /// A device and what can be done with it. <paramref name="trusted"/> is null for an
    /// address typed in, whose pairing is not known: every action is offered there.
    /// </summary>
    private Grid PeerRow(string name, string state, bool? trusted, string? id, string? address = null)
    {
        address ??= name;
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = state, Style = Appearance.Style("BtmSecondaryCaptionStyle") });
        row.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (trusted != true)
        {
            actions.Children.Add(ShareButton("SharePair", accent: trusted == false, () => _ = AskPairingAsync(name, address)));
        }

        if (trusted != false)
        {
            actions.Children.Add(ShareButton("ShareKindText", accent: trusted == true, () => _ = AskTextAsync(name, address)));
            actions.Children.Add(ShareButton("ShareKindFile", accent: false, () => _ = PickAndSendAsync(address)));
        }

        if (trusted == true && id is not null)
        {
            actions.Children.Add(ShareButton("ShareForget", accent: false, () => _sharing.Forget(id)));
        }

        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);
        return row;
    }

    private static Button ShareButton(string key, bool accent, Action click)
    {
        var button = new Button { Content = Strings.Get(key) };
        if (accent)
        {
            button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        }

        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>One thing that came or went: who, what, when, and what can be done with it now.</summary>
    private StackPanel HistoryRow(ShareEvent item)
    {
        var other = item.Type == "received" ? item.From : item.Type == "paired" ? item.Name : item.To;
        other ??= string.Empty;
        var title = item.Type switch
        {
            "received" => Strings.Format("ShareFrom", other),
            "sent" => Strings.Format("ShareTo", other),
            "paired" => Strings.Format("SharePairedWith", other),
            _ => Strings.Format("ShareNotSent", other),
        };
        var detail = item.Type switch
        {
            "paired" => null,
            "failed" => item.NeedsPairing ? Strings.Get("ShareNeedsPairing") : item.Error,
            _ when item.Kind == "text" => item.Text ?? Strings.Get("ShareKindText"),
            _ => item.Name,
        };

        var row = new StackPanel { Spacing = 4 };
        var head = new Grid { ColumnSpacing = 12 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis });
        var time = new TextBlock { Text = item.At.ToString("t"), Style = Appearance.Style("BtmSecondaryCaptionStyle") };
        Grid.SetColumn(time, 1);
        head.Children.Add(time);
        row.Children.Add(head);

        if (!string.IsNullOrEmpty(detail))
        {
            row.Children.Add(new TextBlock
            {
                Text = detail,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 4,
                IsTextSelectionEnabled = item.Kind == "text",
                Style = Appearance.Style(item.Type == "failed" ? "BtmCriticalTextStyle" : "BtmSecondaryTextStyle"),
            });
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (item.Type == "received" && item.Text is { } text)
        {
            actions.Children.Add(ShareButton("ShareCopy", accent: false, () =>
            {
                var package = new DataPackage();
                package.SetText(text);
                Clipboard.SetContent(package);
            }));
        }

        if (item.Type == "received" && item.Path is { } path)
        {
            actions.Children.Add(ShareButton("ShareOpen", accent: false, () =>
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })));
            actions.Children.Add(ShareButton("ShareShowInFolder", accent: false, () =>
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })));
        }

        if (item.Type == "failed" && item.NeedsPairing && item.To is { } to)
        {
            actions.Children.Add(ShareButton("SharePair", accent: false, () => _ = AskPairingAsync(to, to)));
        }

        if (actions.Children.Count > 0)
        {
            row.Children.Add(actions);
        }

        return row;
    }

    private ContentDialog ShareDialog(string title, UIElement content, string primary) => new()
    {
        XamlRoot = Root.XamlRoot,
        RequestedTheme = Root.ActualTheme,
        Title = title,
        Content = content,
        PrimaryButtonText = Strings.Get(primary),
        CloseButtonText = Strings.Get("Cancel"),
        DefaultButton = ContentDialogButton.Primary,
    };

    private async Task AskPairingAsync(string name, string address)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = Strings.Format("SharePairHint", name), TextWrapping = TextWrapping.Wrap });
        var code = new TextBox { Header = Strings.Get("ShareCode"), MaxLength = 7, InputScope = NumberScope() };
        panel.Children.Add(code);
        if (await ShareDialog(Strings.Format("SharePairWith", name), panel, "SharePair").ShowAsync() == ContentDialogResult.Primary)
        {
            var digits = new string(code.Text.Where(char.IsAsciiDigit).ToArray());
            if (digits.Length == 6)
            {
                _sharing.Pair(address, digits);
            }
        }
    }

    private async Task AskTextAsync(string name, string address)
    {
        var text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 96, MinWidth = 320 };
        if (await ShareDialog(Strings.Format("ShareTextTo", name), text, "ShareSend").ShowAsync() == ContentDialogResult.Primary
            && text.Text.Length > 0)
        {
            _sharing.SendText(address, text.Text);
        }
    }

    private async Task PickAndSendAsync(string address)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _handle);
        picker.FileTypeFilter.Add("*");
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            _sharing.SendFile(address, file.Path);
        }
    }

    private async void OnShareByAddress(object sender, RoutedEventArgs e)
    {
        var address = new TextBox { Header = Strings.Get("ShareAddress"), PlaceholderText = "192.168.1.20:50000", MinWidth = 280 };
        if (await ShareDialog(Strings.Get("ShareByAddress"), address, "ShareAdd").ShowAsync() == ContentDialogResult.Primary
            && address.Text.Trim().Length > 0)
        {
            _shareAddress = address.Text.Trim();
            ShowSharePage();
        }
    }

    private static Microsoft.UI.Xaml.Input.InputScope NumberScope()
    {
        var scope = new Microsoft.UI.Xaml.Input.InputScope();
        scope.Names.Add(new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.Number));
        return scope;
    }
}
