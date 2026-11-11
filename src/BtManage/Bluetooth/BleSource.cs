using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace BtManage.Bluetooth;

/// <summary>Listens for Bluetooth LE advertisements and passes manufacturer data to the monitor.</summary>
internal sealed class BleSource : IDisposable
{
    private readonly DeviceMonitor _monitor;
    private readonly BluetoothLEAdvertisementWatcher _watcher;

    public BleSource(DeviceMonitor monitor)
    {
        _monitor = monitor;
        // Passive: the levels are in the advertisement itself, no scan request is needed.
        _watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
        _watcher.Received += OnReceived;
        _watcher.Stopped += OnStopped;
        _monitor.Tick += EnsureRunning;
        EnsureRunning();
    }

    public void Dispose()
    {
        _monitor.Tick -= EnsureRunning;
        _watcher.Received -= OnReceived;
        _watcher.Stopped -= OnStopped;
        if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
        {
            _watcher.Stop();
        }
    }

    /// <summary>
    /// Starts the watcher if it is not running. With the radio off the watcher aborts, so this
    /// is retried on every tick until Bluetooth comes back.
    /// </summary>
    private void EnsureRunning()
    {
        switch (_watcher.Status)
        {
            case BluetoothLEAdvertisementWatcherStatus.Started:
                _monitor.SetBluetoothAvailable(true);
                break;
            case BluetoothLEAdvertisementWatcherStatus.Created:
            case BluetoothLEAdvertisementWatcherStatus.Stopped:
            case BluetoothLEAdvertisementWatcherStatus.Aborted:
                try
                {
                    _watcher.Start();
                }
                catch (Exception)
                {
                    // No Bluetooth adapter at all.
                    _monitor.SetBluetoothAvailable(false);
                }

                break;
        }
    }

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        foreach (var section in args.Advertisement.ManufacturerData)
        {
            _monitor.Ingest(args.BluetoothAddress, args.RawSignalStrengthInDBm, section.CompanyId, section.Data.ToArray());
        }
    }

    private void OnStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        if (args.Error != BluetoothError.Success)
        {
            _monitor.SetBluetoothAvailable(false);
        }
    }
}
