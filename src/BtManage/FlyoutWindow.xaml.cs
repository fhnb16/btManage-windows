using System.Runtime.InteropServices;
using BtManage.Bluetooth;
using BtManage.Interop;
using BtManage.Views;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;

namespace BtManage;

/// <summary>
/// The panel that opens above the notification area: the earbuds in use with a level for
/// each bud and the case, then the other devices that are connected or nearby. It closes
/// when it loses focus, like the system flyouts.
/// </summary>
public sealed partial class FlyoutWindow : Window
{
    /// <summary>Width of the panel and its distance from the screen edges, in effective pixels.</summary>
    private const double PanelWidth = 360;
    private const double EdgeMargin = 12;

    /// <summary>
    /// A click on the tray icon first takes the focus away, which hides the panel. Without
    /// this pause the same click would open it again.
    /// </summary>
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(250);

    private readonly DeviceMonitor _monitor;
    private readonly AppSettings _settings;
    private readonly RadioControl _radio;
    private string? _featuredId;
    private string _listSignature = string.Empty;
    private long? _hiddenAt;

    internal FlyoutWindow(DeviceMonitor monitor, AppSettings settings, RadioControl radio)
    {
        _monitor = monitor;
        _settings = settings;
        _radio = radio;
        InitializeComponent();
        Handle = WinRT.Interop.WindowNative.GetWindowHandle(this);

        Title = Strings.Get("AppName");
        AllDevicesButton.Content = Strings.Get("AllDevices");
        TurnOnButton.Content = Strings.Get("TurnOnBluetooth");
        ToolTipService.SetToolTip(SettingsButton, Strings.Get("BluetoothSettings"));
        AutomationProperties.SetName(SettingsButton, Strings.Get("BluetoothSettings"));

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Closing += (_, args) =>
        {
            // Alt+F4 hides the panel; the application quits from the tray menu.
            args.Cancel = true;
            HideFlyout();
        };

        Appearance.ApplyWindow(this, Root, _settings, transient: true);
        _settings.Changed += () => Appearance.ApplyWindow(this, Root, _settings, transient: true);

        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                HideFlyout();
            }
        };
        _monitor.Changed += Refresh;
        _radio.Changed += Refresh;
        _settings.Changed += Refresh;
    }

    /// <summary>The window handle. The tray icon receives its messages through it.</summary>
    internal nint Handle { get; }

    /// <summary>The user asked for the main window, on a device or on none.</summary>
    internal event Action<string?>? OpenRequested;

    internal void Toggle()
    {
        if (AppWindow.IsVisible)
        {
            HideFlyout();
        }
        else if (_hiddenAt is not { } hiddenAt || Environment.TickCount64 - hiddenAt > ReopenGuard.TotalMilliseconds)
        {
            ShowFlyout();
        }
    }

    internal void ShowFlyout()
    {
        _listSignature = string.Empty;
        Render();
        Place();
        AppWindow.Show();
        SetForegroundWindow(Handle);
    }

    private void HideFlyout()
    {
        if (AppWindow.IsVisible)
        {
            AppWindow.Hide();
            _hiddenAt = Environment.TickCount64;
        }
    }

    private void Refresh()
    {
        if (AppWindow.IsVisible)
        {
            Render();
            Place();
        }
    }

    private void Open(string? deviceId)
    {
        HideFlyout();
        OpenRequested?.Invoke(deviceId);
    }

    private void OnFeaturedTapped(object sender, TappedRoutedEventArgs e) => Open(_featuredId);

    private void OnDeviceClick(object sender, ItemClickEventArgs e) =>
        Open((e.ClickedItem as FrameworkElement)?.Tag as string);

    private void OnAllDevicesClick(object sender, RoutedEventArgs e) => Open(null);

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        BluetoothSettings.Open();
        HideFlyout();
    }

    private void OnTurnOnClick(object sender, RoutedEventArgs e) => _ = _radio.SetAsync(on: true);

    private void Render()
    {
        var threshold = _settings.LowThreshold;
        var active = _monitor.Current.Devices.Where(device => device.IsActive).ToList();

        // The earbuds in use get the card; everything else goes into the list.
        var featured = active.FirstOrDefault(device => device.HasEarbudCells);
        _featuredId = featured?.Id;
        FeaturedPanel.Visibility = VisibleIf(featured is not null);
        if (featured is not null)
        {
            Featured.Set(featured, threshold);
        }

        var rest = active.Where(device => device != featured).ToList();
        ListPanel.Visibility = VisibleIf(rest.Count > 0);
        ListPanel.BorderThickness = new Thickness(0, featured is null ? 0 : 1, 0, 0);
        var signature = string.Join('\n', rest.Select(device => DeviceRow.Signature(device, threshold)));
        if (signature != _listSignature)
        {
            // Rebuilt only on a visible change, so hover and focus survive the periodic refresh.
            _listSignature = signature;
            DeviceList.Items.Clear();
            foreach (var device in rest)
            {
                DeviceList.Items.Add(DeviceRow.Create(device, threshold, withStatus: false));
            }
        }

        var off = _radio.State is BluetoothState.Off or BluetoothState.Missing;
        var empty = active.Count == 0 || off;
        EmptyPanel.Visibility = VisibleIf(empty);
        if (empty)
        {
            FeaturedPanel.Visibility = Visibility.Collapsed;
            ListPanel.Visibility = Visibility.Collapsed;
            var (title, hint) = _radio.State switch
            {
                BluetoothState.Off => ("BluetoothOffTitle", "BluetoothOffHint"),
                BluetoothState.Missing => ("BluetoothMissingTitle", "BluetoothMissingHint"),
                _ => ("EmptyTitle", "EmptyHint"),
            };
            EmptyTitle.Text = Strings.Get(title);
            EmptyHint.Text = Strings.Get(hint);
            TurnOnButton.Visibility = VisibleIf(_radio.State == BluetoothState.Off);
        }
    }

    /// <summary>
    /// Sizes the window to its content and puts it in the corner of the work area next to the
    /// notification area: bottom right, or top right when the taskbar is at the top.
    /// </summary>
    private void Place()
    {
        var scale = GetDpiForWindow(Handle) / 96.0;
        Root.Width = PanelWidth;
        Root.Measure(new Size(PanelWidth, double.PositiveInfinity));

        var width = (int)Math.Ceiling(PanelWidth * scale);
        var height = (int)Math.Ceiling(Root.DesiredSize.Height * scale);
        AppWindow.ResizeClient(new SizeInt32(width, height));

        // With the title bar removed ResizeClient still leaves room for one. Measure the
        // client area that came out and take the difference off the window.
        if (GetClientRect(Handle, out var client))
        {
            var outer = AppWindow.Size;
            var excessWidth = client.Right - client.Left - width;
            var excessHeight = client.Bottom - client.Top - height;
            if (excessWidth != 0 || excessHeight != 0)
            {
                AppWindow.Resize(new SizeInt32(outer.Width - excessWidth, outer.Height - excessHeight));
            }
        }

        // The window rectangle is wider than what is drawn: it includes an invisible resize
        // frame. Position by the client area, which is the visible panel.
        var origin = default(POINT);
        if (!GetWindowRect(Handle, out var window) || !ClientToScreen(Handle, ref origin))
        {
            return;
        }

        var insetLeft = origin.X - window.Left;
        var insetTop = origin.Y - window.Top;

        var display = DisplayArea.Primary;
        var work = display.WorkArea;
        var margin = (int)Math.Round(EdgeMargin * scale);

        var taskbarAtTop = work.Y > display.OuterBounds.Y;
        var taskbarAtLeft = work.X > display.OuterBounds.X;
        var x = taskbarAtLeft ? work.X + margin : work.X + work.Width - width - margin;
        var y = taskbarAtTop ? work.Y + margin : work.Y + work.Height - height - margin;
        AppWindow.Move(new PointInt32(x - insetLeft, y - insetTop));
    }

    private static Visibility VisibleIf(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out RECT rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
