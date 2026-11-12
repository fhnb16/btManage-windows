using System.Runtime.InteropServices;
using BtManage.Bluetooth;
using BtManage.Interop;
using BtManage.Updates;
using BtManage.Views;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace BtManage;

/// <summary>
/// The application window: every paired device on the left, the selected one on the right
/// with its batteries, their history, what is known about it and what can be done to it.
/// Settings live under the device list.
/// </summary>
public sealed partial class MainWindow : Window
{
    internal const string SettingsId = "settings";
    private static readonly SizeInt32 DefaultSize = new(980, 680);
    private static readonly int[] Thresholds = [10, 15, 20, 25, 30];

    private readonly DeviceMonitor _monitor;
    private readonly AppSettings _settings;
    private readonly RadioControl _radio;
    private readonly Updater _updater;
    private readonly nint _handle;
    private TextBlock? _updateStatus;
    private Button? _updateButton;
    private HyperlinkButton? _updatePage;

    private string? _selectedId;
    private string? _wantedId;
    private string _listSignature = string.Empty;
    private string? _shownId;
    private bool _canSwitchAudio;
    private bool _busy;
    private bool _updating;
    private ToggleSwitch? _bluetoothSwitch;

    internal MainWindow(DeviceMonitor monitor, AppSettings settings, RadioControl radio, Updater updater)
    {
        _monitor = monitor;
        _settings = settings;
        _radio = radio;
        _updater = updater;
        InitializeComponent();
        _handle = WinRT.Interop.WindowNative.GetWindowHandle(this);

        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        Title = Strings.Get("AppName");
        TitleText.Text = Title;
        TitleIcon.Source = new BitmapImage(new Uri(icon));
        AppWindow.SetIcon(icon);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        Appearance.ApplyBackdrop(this, Root, _settings, transient: false);
        _settings.Changed += () => Appearance.ApplyBackdrop(this, Root, _settings, transient: false);

        AppWindow.Closing += (_, args) =>
        {
            // Closing hides the window; the application lives on in the tray.
            args.Cancel = true;
            AppWindow.Hide();
        };

        BatteryCard.ShowName = false;
        BatteryTitle.Text = Strings.Get("SectionBattery");
        HistoryTitle.Text = Strings.Get("SectionHistory");
        InfoTitle.Text = Strings.Get("SectionInfo");
        NotifyLabel.Text = Strings.Get("DeviceNotifications");
        ForgetItem.Text = Strings.Get("ForgetHistory");
        WindowsSettingsItem.Text = Strings.Get("BluetoothSettings");
        RemoveItem.Text = Strings.Get("RemoveDevice");
        ToolTipService.SetToolTip(MoreButton, Strings.Get("More"));
        FooterList.Items.Add(Row("", Strings.Get("Settings"), SettingsId));
        BuildSettings();

        _monitor.Changed += Refresh;
        _radio.Changed += UpdateBluetoothSwitch;
        _updater.Changed += ShowUpdateState;
        ShowUpdateState();
    }

    /// <summary>An update has been staged; the application must exit for it to be applied.</summary>
    internal event Action? QuitRequested;

    /// <summary>A setting that takes effect only at start was changed.</summary>
    internal event Action? RestartRequested;

    /// <summary>Shows the window in the middle of the screen, on a device if one is given.</summary>
    internal void ShowCentered(string? deviceId)
    {
        if (!AppWindow.IsVisible)
        {
            var scale = GetDpiForWindow(_handle) / 96.0;
            var work = DisplayArea.Primary.WorkArea;
            var width = Math.Min((int)(DefaultSize.Width * scale), work.Width);
            var height = Math.Min((int)(DefaultSize.Height * scale), work.Height);
            AppWindow.MoveAndResize(new RectInt32(
                work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));
        }

        // The device may not be listed yet, right after start; Refresh selects it once it is.
        _wantedId = deviceId ?? _wantedId;
        _listSignature = string.Empty;
        Refresh();
        AppWindow.Show();
        SetForegroundWindow(_handle);
    }

    private void Refresh()
    {
        if (!AppWindow.IsVisible && _listSignature.Length > 0)
        {
            return;
        }

        var devices = _monitor.Current.Devices;
        var threshold = _settings.LowThreshold;

        if (_wantedId is not null && (_wantedId == SettingsId || devices.Any(device => device.Id == _wantedId)))
        {
            _selectedId = _wantedId;
            _wantedId = null;
        }

        // Keep the selection on a device that still exists; otherwise take the first.
        if (_selectedId != SettingsId && devices.All(device => device.Id != _selectedId))
        {
            _selectedId = devices.FirstOrDefault()?.Id;
        }

        var signature = string.Join('\n', devices.Select(device => $"{Group(device)}|{DeviceRow.Signature(device, threshold)}"));
        if (signature != _listSignature)
        {
            _listSignature = signature;
            BuildList(devices, threshold);
        }

        ShowSelection();
    }

    private void BuildList(IReadOnlyList<Device> devices, int threshold)
    {
        _updating = true;
        DeviceList.Items.Clear();
        string? group = null;
        foreach (var device in devices)
        {
            if (Group(device) != group)
            {
                group = Group(device);
                DeviceList.Items.Add(GroupHeader(Strings.Get(group)));
            }

            var row = DeviceRow.Create(device, threshold, withStatus: true);
            DeviceList.Items.Add(row);
            if (device.Id == _selectedId)
            {
                DeviceList.SelectedItem = row;
            }
        }

        FooterList.SelectedIndex = _selectedId == SettingsId ? 0 : -1;
        _updating = false;
    }

    /// <summary>A quiet caption over a group of devices. It cannot be selected.</summary>
    private static ListViewItem GroupHeader(string text) => new()
    {
        IsEnabled = false,
        IsHitTestVisible = false,
        IsTabStop = false,
        MinHeight = 32,
        VerticalContentAlignment = VerticalAlignment.Bottom,
        Padding = new Thickness(16, 0, 12, 4),
        Content = new TextBlock
        {
            Text = text,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        },
    };

    private static string Group(Device device) =>
        device.Connected ? "GroupConnected" : device.Nearby ? "GroupNearby" : "GroupOther";

    private void OnDeviceSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating && (DeviceList.SelectedItem as FrameworkElement)?.Tag is string id)
        {
            _selectedId = id;
            _updating = true;
            FooterList.SelectedIndex = -1;
            _updating = false;
            ShowSelection();
        }
    }

    private void OnFooterSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating && FooterList.SelectedIndex == 0)
        {
            _selectedId = SettingsId;
            _updating = true;
            DeviceList.SelectedItem = null;
            _updating = false;
            ShowSelection();
        }
    }

    private void ShowSelection()
    {
        var device = _selectedId is null or SettingsId ? null : _monitor.Find(_selectedId);
        DevicePanel.Visibility = VisibleIf(device is not null);
        SettingsPanel.Visibility = VisibleIf(_selectedId == SettingsId);
        PlaceholderPanel.Visibility = VisibleIf(device is null && _selectedId != SettingsId);
        if (device is not null)
        {
            ShowDevice(device);
        }
        else if (_selectedId == SettingsId)
        {
            UpdateBluetoothSwitch();
        }
        else
        {
            PlaceholderTitle.Text = Strings.Get("NoDevicesTitle");
            PlaceholderHint.Text = Strings.Get("NoDevicesHint");
        }
    }

    private void ShowDevice(Device device)
    {
        HeaderIcon.Glyph = DeviceText.Glyph(device.Kind);
        HeaderName.Text = DeviceText.Name(device);
        HeaderSummary.Text = DeviceText.Summary(device);

        if (device.Id != _shownId)
        {
            // Whether Windows lets us connect it takes a walk through the audio devices.
            _shownId = device.Id;
            _canSwitchAudio = false;
            _ = ResolveAudioAsync(device);
        }

        ConnectButton.Visibility = VisibleIf(_canSwitchAudio);
        ConnectButton.Content = Strings.Get(device.Connected ? "Disconnect" : "Connect");
        ConnectButton.IsEnabled = !_busy;
        RemoveItem.Visibility = VisibleIf(device.Paired);
        RemoveSeparator.Visibility = VisibleIf(device.Paired);

        var hasCells = device.Cells.Count > 0;
        BatteryCard.Visibility = VisibleIf(hasCells);
        if (hasCells)
        {
            BatteryCard.Set(device, _settings.LowThreshold);
        }

        BatteryNote.Text = !hasCells ? Strings.Get("NoBattery")
            : device.EstimateMin is { } minutes ? DeviceText.Estimate(minutes)
            : string.Empty;
        BatteryNote.Visibility = VisibleIf(BatteryNote.Text.Length > 0);

        var history = _monitor.History(device.Id, HistoryChart.Window + TimeSpan.FromHours(1));
        HistoryPanel.Visibility = VisibleIf(hasCells || history.Count > 0);
        Chart.Set(history, device.IsActive);

        InfoRows.Children.Clear();
        foreach (var (label, value) in Info(device))
        {
            InfoRows.Children.Add(InfoRow(Strings.Get(label), value));
        }

        _updating = true;
        NotifySwitch.IsOn = !_settings.MutedDevices.Contains(device.Id);
        _updating = false;
    }

    private async Task ResolveAudioAsync(Device device)
    {
        var can = await DeviceActions.CanSwitchAudioAsync(device);
        if (_shownId == device.Id && can != _canSwitchAudio)
        {
            _canSwitchAudio = can;
            ShowSelection();
        }
    }

    /// <summary>What is known about a device, as label and value. Unknown facts are left out.</summary>
    private static IEnumerable<(string Label, string Value)> Info(Device device)
    {
        yield return ("InfoType", DeviceText.KindName(device.Kind));
        if (device.Model is { } model)
        {
            yield return ("InfoModel", model);
        }

        if (device.Address is { } address)
        {
            yield return ("InfoAddress", address);
        }

        if (device.Transport is { } transport)
        {
            yield return ("InfoTransport", Strings.Get($"Transport{transport}"));
        }

        if (device.Profiles.Count > 0)
        {
            yield return ("InfoProfiles", string.Join(", ", device.Profiles));
        }

        if (device.Rssi is { } rssi)
        {
            yield return ("InfoSignal", Strings.Format("SignalFormat", rssi));
        }

        if (device is { VendorId: { } vendor, ProductId: { } product })
        {
            yield return ("InfoIds", $"VID {vendor:X4}, PID {product:X4}");
        }

        var sources = device.Cells.Select(cell => cell.Source).Distinct().Select(source => Strings.Get($"Source{source}")).ToList();
        if (sources.Count > 0)
        {
            yield return ("InfoSource", string.Join(", ", sources));
        }

        if (device is { Connected: false, LastConnectedMs: { } last })
        {
            yield return ("InfoLastConnected", DateTimeOffset.FromUnixTimeMilliseconds(last).LocalDateTime.ToString("g"));
        }
    }

    private static Grid InfoRow(string label, string value)
    {
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        if (_selectedId is null || _monitor.Find(_selectedId) is not { } device || _busy)
        {
            return;
        }

        _busy = true;
        ConnectButton.IsEnabled = false;
        try
        {
            var done = device.Connected
                ? await DeviceActions.DisconnectAudioAsync(device)
                : await DeviceActions.ConnectAudioAsync(device);
            if (!done)
            {
                await ShowFailureAsync();
            }
        }
        finally
        {
            // The new state arrives through the device tree a moment later.
            _busy = false;
            ConnectButton.IsEnabled = true;
        }
    }

    private async void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (_selectedId is null || _monitor.Find(_selectedId) is not { } device)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = Strings.Format("RemoveDialogTitle", DeviceText.Name(device)),
            Content = new TextBlock { Text = Strings.Get("RemoveDialogText"), TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = Strings.Get("RemoveDevice"),
            CloseButtonText = Strings.Get("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (await DeviceActions.UnpairAsync(device))
        {
            _monitor.Forget(device.Id);
        }
        else
        {
            await ShowFailureAsync();
        }
    }

    private void OnForgetClick(object sender, RoutedEventArgs e)
    {
        if (_selectedId is not null and not SettingsId)
        {
            _monitor.Forget(_selectedId);
        }
    }

    private void OnWindowsSettingsClick(object sender, RoutedEventArgs e) => BluetoothSettings.Open();

    private void OnNotifyToggled(object sender, RoutedEventArgs e)
    {
        if (_updating || _selectedId is null or SettingsId)
        {
            return;
        }

        if (NotifySwitch.IsOn)
        {
            _settings.MutedDevices.Remove(_selectedId);
        }
        else
        {
            _settings.MutedDevices.Add(_selectedId);
        }

        _settings.Save();
    }

    private async Task ShowFailureAsync() => await new ContentDialog
    {
        XamlRoot = Root.XamlRoot,
        Title = Strings.Get("ActionFailed"),
        Content = new TextBlock { Text = Strings.Get("ActionFailedHint"), TextWrapping = TextWrapping.Wrap },
        CloseButtonText = "OK",
    }.ShowAsync();

    private void BuildSettings()
    {
        void Section(string title) => SettingsPanel.Children.Add(new TextBlock
        {
            Text = Strings.Get(title),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Margin = new Thickness(0, SettingsPanel.Children.Count == 0 ? 0 : 20, 0, 4),
        });

        Section("SettingsGeneral");
        Setting("SettingAutostart", null, Switch(Autostart.IsEnabled, Autostart.Set));
        _bluetoothSwitch = Switch(_radio.State == BluetoothState.On, on => _ = _radio.SetAsync(on));
        Setting("SettingBluetooth", null, _bluetoothSwitch);

        Section("SettingsAppearance");
        Setting("SettingDesign", "SettingRestartHint", Choice(
            [("fluent", "DesignFluent"), ("material", "DesignMaterial")],
            _settings.Design,
            value => SaveAndRestart(() => _settings.Design = value)));
        Setting("SettingTheme", "SettingRestartHint", Choice(
            [("system", "ThemeSystem"), ("light", "ThemeLight"), ("dark", "ThemeDark")],
            _settings.Theme,
            value => SaveAndRestart(() => _settings.Theme = value)));
        Setting("SettingAccent", "SettingRestartHint", Choice(
            [("system", "AccentSystem"), ("bluetooth", "AccentBluetooth")],
            _settings.Accent,
            value => SaveAndRestart(() => _settings.Accent = value)));
        if (!Appearance.IsMaterial(_settings))
        {
            // Material surfaces are opaque, so there is no backdrop to choose.
            Setting("SettingBackdrop", null, Choice(
                [("auto", "BackdropAuto"), ("acrylic", "BackdropAcrylic"), ("mica", "BackdropMica"), ("mica_alt", "BackdropMicaAlt")],
                _settings.Backdrop,
                value => Save(() => _settings.Backdrop = value)));
        }
        Setting("SettingLanguage", "SettingRestartHint", Choice(
            [("system", "LanguageSystem"), ("en", null), ("ru", null)],
            _settings.Language,
            value => SaveAndRestart(() => _settings.Language = value)));

        Section("SettingsNotifications");
        Setting("SettingNotifyArrival", null, Switch(_settings.NotifyOnArrival, on => Save(() => _settings.NotifyOnArrival = on)));
        Setting("SettingNotifyLow", null, Switch(_settings.NotifyOnLow, on => Save(() => _settings.NotifyOnLow = on)));
        var threshold = new ComboBox { MinWidth = 96 };
        foreach (var value in Thresholds)
        {
            threshold.Items.Add(DeviceText.Percent(value));
        }

        threshold.SelectedIndex = Math.Max(0, Array.IndexOf(Thresholds, _settings.LowThreshold));
        threshold.SelectionChanged += (_, _) => Save(() => _settings.LowThreshold = Thresholds[Math.Max(0, threshold.SelectedIndex)]);
        Setting("SettingThreshold", null, threshold);

        Section("SettingsControllers");
        Setting("SettingPlayStation", "SettingPlayStationHint",
            Switch(_settings.PlayStationFullReports, on => Save(() => _settings.PlayStationFullReports = on)));

        Section("SettingsUpdates");
        Setting("SettingAutoUpdate", null, Switch(_settings.CheckUpdates, on => Save(() => _settings.CheckUpdates = on)));
        _updateStatus = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        _updatePage = new HyperlinkButton { Content = Strings.Get("UpdateReleasePage"), Padding = new Thickness(0), Visibility = Visibility.Collapsed };
        _updatePage.Click += (_, _) =>
        {
            if (_updater.Available is { } release && Uri.TryCreate(release.PageUrl, UriKind.Absolute, out var page))
            {
                _ = Windows.System.Launcher.LaunchUriAsync(page);
            }
        };
        _updateButton = new Button { MinWidth = 112 };
        _updateButton.Click += OnUpdateClick;
        var status = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        status.Children.Add(_updateStatus);
        status.Children.Add(_updatePage);
        var update = new Grid { ColumnSpacing = 16, MinHeight = 40 };
        update.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        update.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        update.Children.Add(status);
        _updateButton.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_updateButton, 1);
        update.Children.Add(_updateButton);
        SettingsPanel.Children.Add(new Border
        {
            Style = (Style)Root.Resources["Card"],
            Padding = new Thickness(16, 12, 16, 12),
            Child = update,
        });

        Section("SettingsAbout");
        var version = Updater.CurrentVersion;
        SettingsPanel.Children.Add(new TextBlock
        {
            Text = Strings.Format("AboutVersion", version, Interop.Hub.CoreVersion),
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_updater.State == UpdateState.Available && _updater.Available?.Asset is not null)
        {
            if (await _updater.InstallAsync())
            {
                QuitRequested?.Invoke();
            }
        }
        else
        {
            await _updater.CheckAsync();
        }
    }

    /// <summary>Shows where the updater stands: what it found and what the button does next.</summary>
    private void ShowUpdateState()
    {
        if (_updateStatus is null || _updateButton is null || _updatePage is null)
        {
            return;
        }

        var release = _updater.Available;
        _updateStatus.Text = _updater.State switch
        {
            UpdateState.Checking => Strings.Get("UpdateChecking"),
            UpdateState.UpToDate => Strings.Get("UpdateUpToDate"),
            UpdateState.Available when release is not null => Strings.Format("UpdateAvailable", release.Version),
            UpdateState.Downloading => Strings.Format("UpdateDownloading", _updater.Progress),
            UpdateState.CheckFailed => Strings.Get("UpdateCheckFailed"),
            UpdateState.InstallFailed => Strings.Get("UpdateInstallFailed"),
            _ => Strings.Format("AboutVersion", Updater.CurrentVersion, Interop.Hub.CoreVersion),
        };
        _updatePage.Visibility = VisibleIf(release is not null);
        var installable = _updater.State == UpdateState.Available && release?.Asset is not null;
        _updateButton.Content = Strings.Get(installable ? "UpdateInstall" : "UpdateCheck");
        _updateButton.IsEnabled = _updater.State is not (UpdateState.Checking or UpdateState.Downloading);
    }

    private void SaveAndRestart(Action change)
    {
        change();
        _settings.Save();
        RestartRequested?.Invoke();
    }

    /// <summary>A drop-down over a fixed set of values.</summary>
    /// <param name="options">Value and the key of its label; a null key shows a language by its own name.</param>
    private static ComboBox Choice((string Value, string? Label)[] options, string current, Action<string> changed)
    {
        var box = new ComboBox { MinWidth = 176 };
        foreach (var (value, label) in options)
        {
            box.Items.Add(label is not null ? Strings.Get(label) : value == "ru" ? "Русский" : "English");
        }

        box.SelectedIndex = Math.Max(0, Array.FindIndex(options, option => option.Value == current));
        box.SelectionChanged += (_, _) => changed(options[Math.Max(0, box.SelectedIndex)].Value);
        return box;
    }

    private void Save(Action change)
    {
        change();
        _settings.Save();
        _listSignature = string.Empty;
        Refresh();
    }

    /// <summary>A settings row: the title, an optional explanation, and the control on the right.</summary>
    private void Setting(string title, string? hint, FrameworkElement control)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(new TextBlock { Text = Strings.Get(title), TextWrapping = TextWrapping.Wrap });
        if (hint is not null)
        {
            text.Children.Add(new TextBlock
            {
                Text = Strings.Get(hint),
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var row = new Grid { ColumnSpacing = 16, MinHeight = 40 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        row.Children.Add(control);

        SettingsPanel.Children.Add(new Border
        {
            Style = (Style)Root.Resources["Card"],
            Padding = new Thickness(16, 12, 16, 12),
            Child = row,
        });
    }

    private ToggleSwitch Switch(bool on, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsOn = on, OnContent = string.Empty, OffContent = string.Empty, MinWidth = 0 };
        toggle.Toggled += (_, _) =>
        {
            if (!_updating)
            {
                changed(toggle.IsOn);
            }
        };
        return toggle;
    }

    private void UpdateBluetoothSwitch()
    {
        if (_bluetoothSwitch is null)
        {
            return;
        }

        _updating = true;
        _bluetoothSwitch.IsOn = _radio.State == BluetoothState.On;
        _bluetoothSwitch.IsEnabled = _radio.State is BluetoothState.On or BluetoothState.Off;
        _updating = false;
    }

    private static Grid Row(string glyph, string text, string tag)
    {
        var row = new Grid { ColumnSpacing = 12, MinHeight = 40, Tag = tag };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 16,
            Width = 20,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        return row;
    }

    private static Visibility VisibleIf(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
