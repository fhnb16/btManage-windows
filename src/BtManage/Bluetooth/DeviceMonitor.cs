using BtManage.Interop;
using Microsoft.UI.Dispatching;

namespace BtManage.Bluetooth;

/// <summary>
/// Holds the current device list. Sources feed it from any thread; <see cref="Changed"/> is
/// raised on the UI thread. Also keeps what the core remembers on disk between runs.
/// </summary>
internal sealed class DeviceMonitor : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SaveInterval = TimeSpan.FromMinutes(5);

    private readonly Hub _hub = new();
    private readonly DispatcherQueue _queue;
    private readonly DispatcherQueueTimer _refresh;
    private readonly string? _statePath;
    private long _savedAt = Environment.TickCount64;

    /// <param name="statePath">Where to keep remembered levels, or null to keep nothing.</param>
    public DeviceMonitor(DispatcherQueue queue, string? statePath)
    {
        _queue = queue;
        _statePath = statePath;
        Load();

        _refresh = queue.CreateTimer();
        _refresh.Interval = RefreshInterval;
        _refresh.Tick += (_, _) => Refresh();
        _refresh.Start();
    }

    public Snapshot Current { get; private set; } = Snapshot.Empty;

    /// <summary>The list changed, or the ages in it moved on.</summary>
    public event Action? Changed;

    /// <summary>Raised every few seconds, for sources that poll.</summary>
    public event Action? Tick;

    public Device? Find(string id) => Current.Devices.FirstOrDefault(device => device.Id == id);

    public void Advertisement(ulong address, short rssi, byte kind, ushort id, ReadOnlySpan<byte> data) =>
        Feed(_hub.Advertisement(address, rssi, kind, id, data));

    public void SetSystemDevices(IReadOnlyList<SystemDeviceInfo> devices) => Feed(_hub.SetSystemDevices(devices));

    public void Battery(string deviceId, Source source, int percent, bool charging) =>
        Feed(_hub.Battery(deviceId, source, percent, charging));

    public void HidReport(string deviceId, ushort vendorId, ushort productId, ReadOnlySpan<byte> report) =>
        Feed(_hub.HidReport(deviceId, vendorId, productId, report));

    public IReadOnlyList<RoleHistory> History(string deviceId, TimeSpan window) => _hub.History(deviceId, window);

    /// <summary>Feeds a previously exported state, for demo mode.</summary>
    public void Import(string state) => _hub.Import(state);

    public void Forget(string deviceId)
    {
        _hub.Forget(deviceId);
        Refresh();
    }

    public void Dispose()
    {
        _refresh.Stop();
        Save();
        _hub.Dispose();
    }

    private void Feed(Snapshot? snapshot)
    {
        if (snapshot is not null)
        {
            _queue.TryEnqueue(() => Publish(snapshot));
        }
    }

    private void Refresh()
    {
        if (_hub.IsClosed)
        {
            return;
        }

        Tick?.Invoke();
        Publish(_hub.Current());
        if (Environment.TickCount64 - _savedAt > SaveInterval.TotalMilliseconds)
        {
            Save();
        }
    }

    private void Publish(Snapshot snapshot)
    {
        Current = snapshot;
        Changed?.Invoke();
    }

    private void Load()
    {
        try
        {
            if (_statePath is not null && File.Exists(_statePath))
            {
                _hub.Import(File.ReadAllText(_statePath));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Start without remembered levels.
        }
    }

    private void Save()
    {
        _savedAt = Environment.TickCount64;
        if (_statePath is null || _hub.IsClosed || _hub.Export() is not { } state)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            // Write beside the file and swap, so a crash never leaves half a file.
            var temporary = _statePath + ".tmp";
            File.WriteAllText(temporary, state);
            File.Move(temporary, _statePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Try again at the next interval.
        }
    }
}
