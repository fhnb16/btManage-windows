using System.Runtime.InteropServices;
using BtManage.Bluetooth;
using BtManage.Interop;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;

namespace BtManage;

/// <summary>
/// The panel that opens above the notification area: the nearest device with its batteries,
/// the other devices in range, and a link to the Bluetooth settings. It closes when it loses
/// focus, like the system flyouts.
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
    private long? _hiddenAt;

    internal FlyoutWindow(DeviceMonitor monitor)
    {
        _monitor = monitor;
        InitializeComponent();
        Handle = WinRT.Interop.WindowNative.GetWindowHandle(this);

        Title = Strings.Get("AppName");
        SettingsButton.Content = Strings.Get("BluetoothSettings");

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

        if (DesktopAcrylicController.IsSupported())
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        else
        {
            Root.Background = (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"];
        }

        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                HideFlyout();
            }
        };
        _monitor.Changed += OnDevicesChanged;
    }

    /// <summary>The window handle. The tray icon receives its messages through it.</summary>
    internal nint Handle { get; }

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

    private void OnDevicesChanged()
    {
        if (AppWindow.IsVisible)
        {
            Render();
            Place();
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        BluetoothSettings.Open();
        HideFlyout();
    }

    private void Render()
    {
        var devices = _monitor.Current.Devices;
        var any = devices.Count > 0;
        DevicePanel.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        EmptyPanel.Visibility = any ? Visibility.Collapsed : Visibility.Visible;

        if (!any)
        {
            var off = !_monitor.BluetoothAvailable;
            EmptyTitle.Text = Strings.Get(off ? "BluetoothOffTitle" : "EmptyTitle");
            EmptyHint.Text = Strings.Get(off ? "BluetoothOffHint" : "EmptyHint");
            return;
        }

        Nearest.Set(devices[0]);

        OtherDevices.Children.Clear();
        foreach (var device in devices.Skip(1))
        {
            OtherDevices.Children.Add(OtherDeviceRow(device));
        }

        OtherDevices.Visibility = devices.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A one-line row: the name on the left, the levels on the right.</summary>
    private static Grid OtherDeviceRow(Device device)
    {
        var row = new Grid { Height = 36, ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = DeviceText.Name(device),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var levels = new TextBlock
        {
            Text = DeviceText.Levels(device),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(levels, FontNumeralAlignment.Tabular);
        Grid.SetColumn(levels, 1);

        row.Children.Add(name);
        row.Children.Add(levels);
        return row;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref POINT point);

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
    private static extern bool GetClientRect(nint window, out RECT rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
