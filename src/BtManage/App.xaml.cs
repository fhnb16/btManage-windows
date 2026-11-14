using BtManage.Bluetooth;
using BtManage.Notifications;
using BtManage.Tray;
using BtManage.Updates;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace BtManage;

public partial class App : Application
{
    private readonly Options _options;
    private readonly List<IDisposable> _sources = [];
    private DispatcherQueue? _queue;
    private readonly AppSettings _settings;
    private DeviceMonitor? _monitor;
    private RadioControl? _radio;
    private FlyoutWindow? _flyout;
    private MainWindow? _main;
    private TrayIcon? _tray;
    private Notifier? _notifier;
    private Sharing? _sharing;
    private readonly Updater _updater = new();
    private long _updateCheckedAt = Environment.TickCount64 - UpdateInterval + FirstUpdateCheckDelay;

    /// <summary>Updates are looked for shortly after start and then once a day, in milliseconds.</summary>
    private const long UpdateInterval = 24 * 60 * 60 * 1000;
    private const long FirstUpdateCheckDelay = 30_000;

    internal App(Options options, AppSettings settings)
    {
        _options = options;
        _settings = settings;
        // The theme is set on the content of each window (Appearance.ApplyWindow), which can
        // change while the application runs; the theme of the application could not.
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _queue = DispatcherQueue.GetForCurrentThread();
        // Before the first window, and before the windows hear of a change: they take the
        // resources this sets.
        Appearance.Apply(this, _settings);
        _settings.Changed += () => Appearance.Apply(this, _settings);
        if (_options.UpdateFrom is { } archive)
        {
            // Install from a local archive and come back as the new version.
            if (Updater.InstallFrom(archive))
            {
                Exit();
                return;
            }
        }

        // Demo mode remembers no levels.
        _monitor = new DeviceMonitor(_queue, _options.Demo ? null : AppPaths.State);
        _radio = new RadioControl(_queue);

        _flyout = new FlyoutWindow(_monitor, _settings, _radio);
        _flyout.OpenRequested += ShowMain;

        _notifier = new Notifier(_settings);
        _notifier.Opened += device => _queue.TryEnqueue(() => ShowMain(device));

        // Files and text from other devices with btManage on the same network.
        _sharing = new Sharing(_queue, _settings);
        _sharing.Received += item =>
        {
            var what = item.Kind == "text" ? (item.Text ?? string.Empty) : (item.Name ?? string.Empty);
            if (what.Length > 80)
            {
                what = what[..80];
            }

            _notifier?.Announce(Strings.Get("AppName"), Strings.Format("ShareReceived", what, item.From ?? string.Empty), MainWindow.ShareId);
        };
        Program.ShowRequested += () => _queue.TryEnqueue(() => ShowMain(null));

        _tray = new TrayIcon(_flyout.Handle);
        _tray.Activated += _flyout.Toggle;
        _tray.OpenRequested += () => ShowMain(null);
        _tray.SettingsRequested += BluetoothSettings.Open;
        _tray.ExitRequested += Quit;

        _monitor.Changed += OnDevicesChanged;
        _monitor.Tick += CheckForUpdates;
        if (_options.Demo)
        {
            _sources.Add(new DemoSource(_monitor));
        }
        else
        {
            _sources.Add(new BleSource(_monitor));
            _sources.Add(new SystemDeviceSource(_monitor));
            _sources.Add(new GamepadSource(_monitor, _settings));
        }

        if (_options.Flyout)
        {
            _flyout.ShowFlyout();
        }
        else if (!_options.Tray)
        {
            ShowMain(_options.Open);
        }
    }

    /// <summary>Opens the application window in the middle of the screen.</summary>
    private void ShowMain(string? deviceId)
    {
        if (_main is null)
        {
            _main = new MainWindow(_monitor!, _settings, _radio!, _updater, _sharing!);
            _main.RestartRequested += Restart;
            _main.QuitRequested += Quit;
        }

        _main.ShowCentered(deviceId);
    }

    /// <summary>Starts the application anew, for a language to take effect.</summary>
    private void Restart()
    {
        Appearance.Restart(_options);
        Quit();
    }

    /// <summary>Looks for a newer release once a day and tells the user about each one once.</summary>
    private async void CheckForUpdates()
    {
        if (!_settings.CheckUpdates || _options.Demo || Environment.TickCount64 - _updateCheckedAt < UpdateInterval)
        {
            return;
        }

        _updateCheckedAt = Environment.TickCount64;
        // By now the script of an update that started this process has finished.
        Updater.CleanUp();
        if (await _updater.CheckAsync() is { } release && release.Version != _settings.AnnouncedVersion)
        {
            _settings.AnnouncedVersion = release.Version;
            _settings.Save();
            _notifier?.Announce(Strings.Get("AppName"), Strings.Format("UpdateAvailable", release.Version), MainWindow.SettingsId);
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
        foreach (var source in _sources)
        {
            source.Dispose();
        }

        _monitor?.Dispose();
        _tray?.Dispose();
        _notifier?.Dispose();
        _sharing?.Dispose();
        Exit();
    }
}
