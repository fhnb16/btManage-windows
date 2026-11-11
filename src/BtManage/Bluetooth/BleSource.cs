using System.Runtime.InteropServices.WindowsRuntime;
using BtManage.Interop;
using Windows.Devices.Bluetooth.Advertisement;

namespace BtManage.Bluetooth;

/// <summary>
/// Listens for Bluetooth LE advertisements and passes manufacturer data and 16-bit service
/// data to the monitor. That is where earbuds put the levels of each bud and the case.
/// </summary>
internal sealed class BleSource : IDisposable
{
    /// <summary>Advertising data type "Service Data - 16-bit UUID".</summary>
    private const byte ServiceData16 = 0x16;

    private readonly DeviceMonitor _monitor;
    private readonly BluetoothLEAdvertisementWatcher _watcher;

    public BleSource(DeviceMonitor monitor)
    {
        _monitor = monitor;
        // Passive: the levels are in the advertisement itself, no scan request is needed.
        _watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
        _watcher.Received += OnReceived;
        _monitor.Tick += EnsureRunning;
        EnsureRunning();
    }

    public void Dispose()
    {
        _monitor.Tick -= EnsureRunning;
        _watcher.Received -= OnReceived;
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
        if (_watcher.Status is BluetoothLEAdvertisementWatcherStatus.Created
            or BluetoothLEAdvertisementWatcherStatus.Stopped
            or BluetoothLEAdvertisementWatcherStatus.Aborted)
        {
            try
            {
                _watcher.Start();
            }
            catch (Exception)
            {
                // No Bluetooth adapter; RadioControl reports that to the interface.
            }
        }
    }

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var address = args.BluetoothAddress;
        var rssi = args.RawSignalStrengthInDBm;
        try
        {
            foreach (var section in args.Advertisement.ManufacturerData)
            {
                _monitor.Advertisement(address, rssi, Hub.ManufacturerData, section.CompanyId, section.Data.ToArray());
            }

            foreach (var section in args.Advertisement.GetSectionsByType(ServiceData16))
            {
                var data = section.Data.ToArray();
                if (data.Length > 2)
                {
                    var uuid = (ushort)(data[0] | data[1] << 8);
                    _monitor.Advertisement(address, rssi, Hub.ServiceData, uuid, data.AsSpan(2));
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // An advertisement that arrived while the application was shutting down.
        }
    }
}
