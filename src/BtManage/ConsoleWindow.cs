using System.Text;
using BtManage.Bluetooth;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace BtManage;

/// <summary>
/// The developer console: raw traffic with one device, over GATT or a serial channel, with
/// ready commands to start from and everything that goes back and forth in one log.
/// </summary>
internal sealed class ConsoleWindow : Window
{
    private const int MaxLines = 2000;

    /// <summary>A ready command: where it goes and what it sends.</summary>
    /// <param name="Label">Key of its name.</param>
    /// <param name="Gatt">GATT rather than a serial channel.</param>
    /// <param name="Target">The characteristic (GATT) or the service (serial).</param>
    /// <param name="Data">What to send, empty for none.</param>
    /// <param name="Hex">The data is hexadecimal.</param>
    /// <param name="Action">"read", "subscribe" or "send".</param>
    private sealed record Template(string Label, bool Gatt, string Target, string Data, bool Hex, string Action);

    private const string UartWrite = "6e400002-b5a3-f393-e0a9-e50e24dcca9e";
    private const string UartRead = "6e400003-b5a3-f393-e0a9-e50e24dcca9e";
    private const string Spp = "1101";
    private const string FastPairStream = "df21fe2c-2515-4fdb-8886-f12c4d67927c";

    private static readonly Template[] Templates =
    [
        new("ConsoleTplBattery", true, "2A19", string.Empty, false, "read"),
        new("ConsoleTplBatteryNotify", true, "2A19", string.Empty, false, "subscribe"),
        new("ConsoleTplName", true, "2A00", string.Empty, false, "read"),
        new("ConsoleTplMaker", true, "2A29", string.Empty, false, "read"),
        new("ConsoleTplModel", true, "2A24", string.Empty, false, "read"),
        new("ConsoleTplFirmware", true, "2A26", string.Empty, false, "read"),
        new("ConsoleTplSerial", true, "2A25", string.Empty, false, "read"),
        new("ConsoleTplHeartRate", true, "2A37", string.Empty, false, "subscribe"),
        new("ConsoleTplUartListen", true, UartRead, string.Empty, false, "subscribe"),
        new("ConsoleTplUartBattery", true, UartWrite, "Battery;", false, "send"),
        new("ConsoleTplAt", false, Spp, "AT", false, "send"),
        new("ConsoleTplAtVersion", false, Spp, "AT+VERSION?", false, "send"),
        new("ConsoleTplAtName", false, Spp, "AT+NAME?", false, "send"),
        new("ConsoleTplAtBattery", false, Spp, "AT+CBC", false, "send"),
        new("ConsoleTplFastPairRing", false, FastPairStream, "04 01 00 01 03", true, "send"),
    ];

    private readonly DeviceMonitor _monitor;
    private readonly AppSettings _settings;
    private readonly DeviceConsole _console = new();
    private readonly Grid _root;
    private readonly ComboBox _device = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _way = new() { MinWidth = 120 };
    private readonly ComboBox _template = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _characteristics = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _target = new();
    private readonly TextBox _data = new();
    private readonly CheckBox _hex = new();
    private readonly ComboBox _ending = new() { MinWidth = 96 };
    private readonly Button _connect = new();
    private readonly Button _close = new();
    private readonly Button _send = new();
    private readonly Button _read = new();
    private readonly Button _subscribe = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _output = new();
    private readonly ScrollViewer _scroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private StackPanel? _characteristicsField;
    private readonly StringBuilder _log = new();
    private List<Interop.Device> _devices = [];
    private int _lines;

    public ConsoleWindow(DeviceMonitor monitor, AppSettings settings)
    {
        _monitor = monitor;
        _settings = settings;
        Title = Strings.Get("ConsoleTitle");
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        AppWindow.Resize(new SizeInt32(760, 720));

        _root = new Grid { Style = Appearance.Style("BtmWindowRootStyle") };
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var titleBar = new Grid { Padding = new Thickness(16, 0, 0, 0) };
        titleBar.Children.Add(new TextBlock
        {
            Text = Title,
            Style = Appearance.Style("BtmSecondaryCaptionStyle"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        _root.Children.Add(titleBar);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBar);

        var form = BuildForm();
        Grid.SetRow(form, 1);
        _root.Children.Add(form);

        var log = BuildLog();
        Grid.SetRow(log, 2);
        _root.Children.Add(log);

        Content = _root;
        Appearance.ApplyWindow(this, _root, _settings, transient: false);
        _settings.Changed += OnSettingsChanged;
        _root.ActualThemeChanged += (_, _) => Appearance.ColorCaptionButtons(AppWindow, _root.ActualTheme);
        Appearance.ColorCaptionButtons(AppWindow, _root.ActualTheme);

        var queue = DispatcherQueue;
        _console.Line += line => queue.TryEnqueue(() => Write(line));
        _monitor.Changed += FillDevices;
        Closed += (_, _) =>
        {
            _monitor.Changed -= FillDevices;
            _settings.Changed -= OnSettingsChanged;
            _console.Dispose();
        };

        FillDevices();
        ShowWay();
        ShowConnection();
    }

    private void OnSettingsChanged() => Appearance.ApplyWindow(this, _root, _settings, transient: false);

    private StackPanel BuildForm()
    {
        var form = new StackPanel { Spacing = 12, Padding = new Thickness(16, 8, 16, 12) };

        // Where to: the device, the way, and the connection.
        _way.Items.Add("GATT");
        _way.Items.Add(Strings.Get("ConsoleWaySerial"));
        _way.SelectedIndex = 0;
        _way.SelectionChanged += (_, _) => ShowWay();
        _device.SelectionChanged += (_, _) => PickWayForDevice();
        _connect.Content = Strings.Get("ConsoleConnect");
        _connect.Click += async (_, _) => await ConnectAsync();
        _close.Content = Strings.Get("ConsoleDisconnect");
        _close.Click += (_, _) =>
        {
            _console.Close();
            _characteristics.Items.Clear();
            Write(Strings.Get("ConsoleClosed"));
            ShowConnection();
        };
        var where = Row([_device, _way, _connect, _close], stretch: 0);
        form.Children.Add(Labeled("ConsoleDevice", where));
        _status.Style = Appearance.Style("BtmSecondaryCaptionStyle");
        form.Children.Add(_status);

        // A ready command fills the fields below; nothing is sent until asked.
        _template.SelectionChanged += (_, _) => ApplyTemplate();
        form.Children.Add(Labeled("ConsoleTemplate", _template));

        _characteristics.PlaceholderText = Strings.Get("ConsoleCharacteristicsEmpty");
        _characteristics.SelectionChanged += (_, _) =>
        {
            if (_characteristics.SelectedItem is string uuid)
            {
                _target.Text = uuid;
            }
        };
        _characteristicsField = Labeled("ConsoleCharacteristics", _characteristics);
        form.Children.Add(_characteristicsField);
        form.Children.Add(Labeled("ConsoleTarget", _target));

        _hex.Content = Strings.Get("ConsoleHex");
        _hex.MinWidth = 0;
        _ending.Items.Add(Strings.Get("ConsoleEndingNone"));
        _ending.Items.Add("CR");
        _ending.Items.Add("CR LF");
        _ending.Items.Add("LF");
        _ending.SelectedIndex = 0;
        ToolTipService.SetToolTip(_ending, Strings.Get("ConsoleEnding"));
        _data.FontFamily = Mono;
        _data.PlaceholderText = Strings.Get("ConsoleDataHint");
        form.Children.Add(Labeled("ConsoleData", Row([_data, _hex, _ending], stretch: 0)));

        _send.Content = Strings.Get("ConsoleSend");
        _send.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        _send.Click += async (_, _) => await SendAsync();
        _read.Content = Strings.Get("ConsoleRead");
        _read.Click += async (_, _) => await GattAsync(read: true);
        _subscribe.Content = Strings.Get("ConsoleSubscribe");
        _subscribe.Click += async (_, _) => await GattAsync(read: false);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(_send);
        actions.Children.Add(_read);
        actions.Children.Add(_subscribe);
        form.Children.Add(actions);
        return form;
    }

    private Grid BuildLog()
    {
        var grid = new Grid { Padding = new Thickness(16, 0, 16, 16), RowSpacing = 8 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = Strings.Get("ConsoleOutput"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        var copy = new Button { Content = Strings.Get("ConsoleCopy") };
        copy.Click += (_, _) =>
        {
            var package = new DataPackage();
            package.SetText(_log.ToString());
            Clipboard.SetContent(package);
        };
        var clear = new Button { Content = Strings.Get("ConsoleClear") };
        clear.Click += (_, _) =>
        {
            _log.Clear();
            _lines = 0;
            _output.Text = string.Empty;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(copy);
        buttons.Children.Add(clear);
        Grid.SetColumn(buttons, 1);
        header.Children.Add(buttons);
        grid.Children.Add(header);

        _output.IsTextSelectionEnabled = true;
        _output.TextWrapping = TextWrapping.Wrap;
        _output.FontFamily = Mono;
        _output.Style = Appearance.Style("BtmPrimaryTextStyle");
        _scroller.Content = _output;
        Grid.SetRow(_scroller, 1);
        grid.Children.Add(_scroller);
        return grid;
    }

    private static FontFamily Mono { get; } = new("Cascadia Mono, Consolas");

    /// <summary>A field with its name above it.</summary>
    private static StackPanel Labeled(string label, FrameworkElement field)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = Strings.Get(label), Style = Appearance.Style("BtmSecondaryCaptionStyle") });
        panel.Children.Add(field);
        return panel;
    }

    /// <summary>Elements side by side; the one at <paramref name="stretch"/> takes the room left.</summary>
    private static Grid Row(FrameworkElement[] elements, int stretch)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        for (var i = 0; i < elements.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = i == stretch ? new GridLength(1, GridUnitType.Star) : GridLength.Auto,
            });
            elements[i].VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(elements[i], i);
            grid.Children.Add(elements[i]);
        }

        return grid;
    }

    private bool Gatt => _way.SelectedIndex == 0;

    private Interop.Device? Selected => _device.SelectedIndex >= 0 && _device.SelectedIndex < _devices.Count
        ? _devices[_device.SelectedIndex]
        : null;

    /// <summary>The devices with an address, kept in place while the list does not change.</summary>
    private void FillDevices()
    {
        var devices = _monitor.Current.Devices
            .Where(device => DeviceConsole.Address(device.Address) is not null)
            .OrderByDescending(device => device.Connected)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (devices.Select(device => device.Id).SequenceEqual(_devices.Select(device => device.Id)))
        {
            return;
        }

        var selected = Selected?.Id;
        _devices = devices;
        _device.Items.Clear();
        foreach (var device in devices)
        {
            _device.Items.Add($"{device.Name}  {device.Address}");
        }

        _device.SelectedIndex = Math.Max(0, devices.FindIndex(device => device.Id == selected));
    }

    /// <summary>A Bluetooth LE device goes over GATT, a Classic one over a serial channel.</summary>
    private void PickWayForDevice()
    {
        if (Selected is { } device && device.Transport is { } transport)
        {
            _way.SelectedIndex = transport == Interop.Transport.Classic ? 1 : 0;
        }
    }

    private void ShowWay()
    {
        var gatt = Gatt;
        _template.Items.Clear();
        _template.Items.Add(Strings.Get("ConsoleTemplateNone"));
        foreach (var template in Templates.Where(template => template.Gatt == gatt))
        {
            _template.Items.Add(Strings.Get(template.Label));
        }

        _template.SelectedIndex = 0;
        _target.PlaceholderText = Strings.Get(gatt ? "ConsoleTargetGattHint" : "ConsoleTargetSerialHint");
        if (!gatt && _target.Text.Length == 0)
        {
            _target.Text = Spp;
        }

        var gattOnly = gatt ? Visibility.Visible : Visibility.Collapsed;
        if (_characteristicsField is not null)
        {
            _characteristicsField.Visibility = gattOnly;
        }

        _read.Visibility = gattOnly;
        _subscribe.Visibility = _read.Visibility;
        _ending.SelectedIndex = gatt ? 0 : 2;
    }

    private void ApplyTemplate()
    {
        var gatt = Gatt;
        var index = _template.SelectedIndex - 1;
        var templates = Templates.Where(template => template.Gatt == gatt).ToList();
        if (index < 0 || index >= templates.Count)
        {
            return;
        }

        var chosen = templates[index];
        _target.Text = chosen.Target;
        _data.Text = chosen.Data;
        _hex.IsChecked = chosen.Hex;
        Write(Strings.Format("ConsoleTemplateHint", Strings.Get(chosen.Action switch
        {
            "read" => "ConsoleRead",
            "subscribe" => "ConsoleSubscribe",
            _ => "ConsoleSend",
        })));
    }

    private async Task ConnectAsync()
    {
        if (Selected is not { } device || DeviceConsole.Address(device.Address) is not { } address)
        {
            return;
        }

        _connect.IsEnabled = false;
        _characteristics.Items.Clear();
        if (Gatt)
        {
            Write(Strings.Format("ConsoleConnecting", device.Name));
            await _console.ConnectGattAsync(address);
            foreach (var characteristic in _console.Characteristics)
            {
                _characteristics.Items.Add(DeviceConsole.Name(characteristic.Uuid));
            }
        }
        else if (DeviceConsole.Uuid(_target.Text) is { } service)
        {
            Write(Strings.Format("ConsoleConnecting", device.Name));
            await _console.ConnectRfcommAsync(address, service);
        }
        else
        {
            Write(Strings.Get("ConsoleBadService"));
        }

        _connect.IsEnabled = true;
        ShowConnection();
    }

    private async Task SendAsync()
    {
        if (Bytes() is not { } data)
        {
            return;
        }

        if (Gatt)
        {
            if (DeviceConsole.Uuid(_target.Text) is { } uuid)
            {
                await _console.WriteAsync(uuid, data);
            }
            else
            {
                Write(Strings.Get("ConsoleBadCharacteristic"));
            }
        }
        else
        {
            await _console.SendRfcommAsync(data);
        }

        ShowConnection();
    }

    private async Task GattAsync(bool read)
    {
        if (DeviceConsole.Uuid(_target.Text) is not { } uuid)
        {
            Write(Strings.Get("ConsoleBadCharacteristic"));
            return;
        }

        if (read)
        {
            await _console.ReadAsync(uuid);
        }
        else
        {
            await _console.SubscribeAsync(uuid);
        }
    }

    /// <summary>The data field as bytes: hexadecimal, or text with the chosen line ending.</summary>
    private byte[]? Bytes()
    {
        if (_hex.IsChecked == true)
        {
            var bytes = DeviceConsole.ParseHex(_data.Text);
            if (bytes is null)
            {
                Write(Strings.Get("ConsoleBadHex"));
            }

            return bytes;
        }

        var ending = _ending.SelectedIndex switch
        {
            1 => "\r",
            2 => "\r\n",
            3 => "\n",
            _ => string.Empty,
        };
        return Encoding.UTF8.GetBytes(_data.Text + ending);
    }

    private void ShowConnection()
    {
        var connected = _console.Connection.Length > 0;
        _status.Text = connected ? _console.Connection : Strings.Get("ConsoleNotConnected");
        _close.IsEnabled = connected;
    }

    private void Write(string line)
    {
        var stamped = $"{DateTime.Now:HH:mm:ss.fff}  {line}";
        _log.AppendLine(stamped);
        _lines++;
        if (_lines > MaxLines)
        {
            // Keep the log to its last lines, so a chatty device does not slow the window down.
            var text = _log.ToString();
            var cut = text.IndexOf('\n', text.Length / 4);
            _log.Clear().Append(cut >= 0 ? text[(cut + 1)..] : text);
            _lines = _log.ToString().Count(c => c == '\n');
        }

        _output.Text = _log.ToString();
        _scroller.UpdateLayout();
        _scroller.ChangeView(null, _scroller.ScrollableHeight, null, disableAnimation: true);
        ShowConnection();
    }
}
