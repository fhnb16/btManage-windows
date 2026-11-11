using BtManage.Interop;
using Microsoft.UI.Dispatching;

namespace BtManage.Bluetooth;

/// <summary>
/// Holds the current state of the nearby devices. Sources feed it advertisements from any
/// thread; <see cref="Changed"/> is raised on the UI thread.
/// </summary>
internal sealed class DeviceMonitor : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    private readonly Tracker _tracker = new();
    private readonly DispatcherQueue _queue;
    private readonly DispatcherQueueTimer _refresh;

    public DeviceMonitor(DispatcherQueue queue)
    {
        _queue = queue;
        _refresh = queue.CreateTimer();
        _refresh.Interval = RefreshInterval;
        _refresh.Tick += (_, _) => Refresh();
        _refresh.Start();
    }

    public Snapshot Current { get; private set; } = Snapshot.Empty;

    /// <summary>False while the Bluetooth radio is off or missing.</summary>
    public bool BluetoothAvailable { get; private set; } = true;

    /// <summary>The state or the availability of Bluetooth changed, or ages moved on.</summary>
    public event Action? Changed;

    /// <summary>Raised every few seconds, for sources that need to retry.</summary>
    public event Action? Tick;

    public void Ingest(ulong address, short rssi, ushort companyId, ReadOnlySpan<byte> data)
    {
        try
        {
            if (_tracker.Ingest(address, rssi, companyId, data, Environment.TickCount64) is { } snapshot)
            {
                _queue.TryEnqueue(() => Publish(snapshot));
            }
        }
        catch (ObjectDisposedException)
        {
            // An advertisement that arrived while the application was shutting down.
        }
    }

    public void SetBluetoothAvailable(bool available)
    {
        _queue.TryEnqueue(() =>
        {
            if (BluetoothAvailable != available)
            {
                BluetoothAvailable = available;
                Changed?.Invoke();
            }
        });
    }

    public void Dispose()
    {
        _refresh.Stop();
        _tracker.Dispose();
    }

    private void Refresh()
    {
        Tick?.Invoke();
        if (!_tracker.IsClosed)
        {
            Publish(_tracker.Current(Environment.TickCount64));
        }
    }

    private void Publish(Snapshot snapshot)
    {
        Current = snapshot;
        Changed?.Invoke();
    }
}
