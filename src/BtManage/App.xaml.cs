using BtManage.Bluetooth;
using BtManage.Notifications;
using BtManage.Tray;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace BtManage;

public partial class App : Application
{
    private readonly Options _options;
    private DeviceMonitor? _monitor;
    private IDisposable? _source;
    private FlyoutWindow? _flyout;
    private TrayIcon? _tray;
    private Notifier? _notifier;

    internal App(Options options)
    {
        _options = options;
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _monitor = new DeviceMonitor(DispatcherQueue.GetForCurrentThread());
        _flyout = new FlyoutWindow(_monitor);
        _notifier = new Notifier();

        _tray = new TrayIcon(_flyout.Handle);
        _tray.Activated += _flyout.Toggle;
        _tray.SettingsRequested += BluetoothSettings.Open;
        _tray.ExitRequested += Quit;

        _monitor.Changed += OnDevicesChanged;
        _source = _options.Demo ? new DemoSource(_monitor) : new BleSource(_monitor);

        if (_options.Show)
        {
            _flyout.ShowFlyout();
        }
    }

    private void OnDevicesChanged()
    {
        var snapshot = _monitor!.Current;
        _tray?.SetTooltip(DeviceText.Tooltip(snapshot));
        _notifier?.Update(snapshot);
    }

    private void Quit()
    {
        _source?.Dispose();
        _monitor?.Dispose();
        _tray?.Dispose();
        _notifier?.Dispose();
        Exit();
    }
}
