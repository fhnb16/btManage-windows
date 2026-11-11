using System.Globalization;
using System.Text.RegularExpressions;
using BtManage.Interop;
using Windows.Devices.Enumeration;

namespace BtManage.Bluetooth;

/// <summary>
/// Reads the Bluetooth devices paired with Windows from the device tree: names, addresses,
/// connection state, device class, vendor and product identifiers, services, and the battery
/// level Windows itself keeps (the one the Settings app shows).
///
/// Every paired device is a container of device nodes. The node "BTHENUM\DEV_address"
/// (Classic) or "BTHLE\DEV_address" (LE) stands for the device itself; one more node per
/// service hangs below it, and the instance identifiers of those carry the service UUID and
/// the vendor and product identifiers.
/// </summary>
internal sealed partial class SystemDeviceSource : IDisposable
{
    private const string InstanceIdKey = "System.Devices.DeviceInstanceId";
    private const string ContainerIdKey = "System.Devices.ContainerId";
    private const string DisplayNameKey = "System.ItemNameDisplay";
    private const string IsConnectedKey = "{83DA6326-97A6-4088-9453-A1923F573B29} 15";
    private const string ClassOfDeviceKey = "{2BD67D8B-8BEB-48D5-87E0-6CDA3428040A} 4";
    private const string LastConnectedKey = "{2BD67D8B-8BEB-48D5-87E0-6CDA3428040A} 5";
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    /// <summary>Every node the Bluetooth enumerators create.</summary>
    private const string Selector = "System.Devices.DeviceInstanceId:~<\"BTH\"";

    private static readonly string[] Properties =
        [InstanceIdKey, ContainerIdKey, DisplayNameKey, IsConnectedKey, ClassOfDeviceKey, LastConnectedKey, BatteryKey];

    /// <summary>A change in the tree is followed by more; wait for them before rescanning.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(600);

    /// <summary>Rescan this often even without a change, as a safety net.</summary>
    private const int TicksPerScan = 12;

    private readonly DeviceMonitor _monitor;
    private readonly DeviceWatcher _watcher;
    private readonly Timer _debounce;
    private int _ticks;
    private int _scanning;

    public SystemDeviceSource(DeviceMonitor monitor)
    {
        _monitor = monitor;
        _debounce = new Timer(_ => _ = ScanAsync());

        _watcher = DeviceInformation.CreateWatcher(Selector, Properties, DeviceInformationKind.Device);
        _watcher.Added += (_, _) => Schedule();
        _watcher.Updated += (_, _) => Schedule();
        _watcher.Removed += (_, _) => Schedule();
        _watcher.Start();

        _monitor.Tick += OnTick;
        Schedule();
    }

    public void Dispose()
    {
        _monitor.Tick -= OnTick;
        _debounce.Dispose();
        if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
        {
            _watcher.Stop();
        }
    }

    /// <summary>Reads the paired devices once.</summary>
    public static async Task<IReadOnlyList<SystemDeviceInfo>> ReadAsync()
    {
        var nodes = await DeviceInformation.FindAllAsync(Selector, Properties, DeviceInformationKind.Device);
        var containers = nodes
            .Select(Node.From)
            .Where(node => node.Container is not null)
            .GroupBy(node => node.Container!.Value)
            .ToList();

        s_nodes = containers.ToDictionary(
            container => container.Key.ToString("D"),
            container => (IReadOnlySet<string>)container
                .Select(node => node.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase));

        return containers.Select(Describe).OfType<SystemDeviceInfo>().ToList();
    }

    /// <summary>
    /// Instance identifiers of the device nodes that make up a device, as of the last scan.
    /// Audio filters are matched to a device through them.
    /// </summary>
    public static IReadOnlySet<string> NodesOf(string deviceId) =>
        s_nodes.TryGetValue(deviceId, out var nodes) ? nodes : EmptyNodes;

    private static readonly IReadOnlySet<string> EmptyNodes = new HashSet<string>();
    private static volatile IReadOnlyDictionary<string, IReadOnlySet<string>> s_nodes =
        new Dictionary<string, IReadOnlySet<string>>();

    private void OnTick()
    {
        if (++_ticks % TicksPerScan == 0)
        {
            Schedule();
        }
    }

    private void Schedule()
    {
        try
        {
            _debounce.Change(Settle, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // A tree change that arrived while the application was shutting down.
        }
    }

    private async Task ScanAsync()
    {
        if (Interlocked.Exchange(ref _scanning, 1) == 1)
        {
            Schedule();
            return;
        }

        try
        {
            _monitor.SetSystemDevices(await ReadAsync());
        }
        catch (Exception)
        {
            // The device tree can be read again at the next change or tick.
        }
        finally
        {
            Interlocked.Exchange(ref _scanning, 0);
        }
    }

    /// <summary>Puts the nodes of one container together into a device, if it is one.</summary>
    private static SystemDeviceInfo? Describe(IGrouping<Guid, Node> container)
    {
        var classic = container.FirstOrDefault(node => node.Id.StartsWith(@"BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase));
        var le = container.FirstOrDefault(node => node.Id.StartsWith(@"BTHLE\DEV_", StringComparison.OrdinalIgnoreCase));
        var root = classic ?? le;
        if (root is null)
        {
            // The radio and its enumerators, not a paired device.
            return null;
        }

        byte? vendorSource = null;
        ushort? vendorId = null;
        ushort? productId = null;
        var services = new SortedSet<ushort>();
        foreach (var node in container)
        {
            if (vendorId is null && VendorPattern().Match(node.Id) is { Success: true } vendor)
            {
                vendorSource = byte.Parse(vendor.Groups["source"].ValueSpan, NumberStyles.HexNumber);
                vendorId = ushort.Parse(vendor.Groups["vendor"].ValueSpan, NumberStyles.HexNumber);
                productId = ushort.Parse(vendor.Groups["product"].ValueSpan, NumberStyles.HexNumber);
            }

            if (ServicePattern().Match(node.Id) is { Success: true } service)
            {
                services.Add(ushort.Parse(service.Groups["uuid"].ValueSpan, NumberStyles.HexNumber));
            }
        }

        var connected = container.Any(node => node.IsRoot && node.Connected);
        return new SystemDeviceInfo
        {
            Id = container.Key.ToString("D"),
            Name = root.Name,
            Address = AddressPattern().Match(root.Id) is { Success: true } address
                ? ulong.Parse(address.Groups["address"].ValueSpan, NumberStyles.HexNumber)
                : null,
            Transport = (classic, le) switch
            {
                (not null, not null) => "dual",
                (not null, null) => "classic",
                _ => "le",
            },
            Connected = connected,
            ClassOfDevice = classic?.ClassOfDevice,
            VendorSource = vendorSource,
            VendorId = vendorId,
            ProductId = productId,
            Services = [.. services],
            Battery = container.Select(node => node.Battery).FirstOrDefault(level => level is not null),
            LastConnectedMs = connected ? null : container.Max(node => node.LastConnected)?.ToUnixTimeMilliseconds(),
        };
    }

    // "BTHENUM\DEV_80AF1919B477\…" and "BTHLE\DEV_…".
    [GeneratedRegex(@"\\DEV_(?<address>[0-9A-F]{12})", RegexOptions.IgnoreCase)]
    private static partial Regex AddressPattern();

    // Classic: "VID&0001004C_PID&201B" (four digits of source). LE: "VID&02045E_PID&0B13" (two).
    [GeneratedRegex(@"VID&(?:00)?(?<source>0[12])(?<vendor>[0-9A-F]{4})_PID&(?<product>[0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VendorPattern();

    // "{0000110B-0000-1000-8000-00805F9B34FB}": a 16-bit UUID on the Bluetooth base.
    [GeneratedRegex(@"\{0000(?<uuid>[0-9A-F]{4})-0000-1000-8000-00805F9B34FB\}", RegexOptions.IgnoreCase)]
    private static partial Regex ServicePattern();

    private sealed record Node(
        string Id,
        Guid? Container,
        string Name,
        bool Connected,
        uint? ClassOfDevice,
        DateTimeOffset? LastConnected,
        byte? Battery)
    {
        public bool IsRoot =>
            Id.StartsWith(@"BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase)
            || Id.StartsWith(@"BTHLE\DEV_", StringComparison.OrdinalIgnoreCase);

        public static Node From(DeviceInformation information)
        {
            var properties = information.Properties;
            return new Node(
                Id: Get<string>(InstanceIdKey) ?? string.Empty,
                Container: Get<Guid?>(ContainerIdKey),
                Name: Get<string>(DisplayNameKey) ?? information.Name,
                Connected: Get<bool?>(IsConnectedKey) ?? false,
                ClassOfDevice: Get<uint?>(ClassOfDeviceKey),
                LastConnected: Get<DateTimeOffset?>(LastConnectedKey),
                Battery: Get<byte?>(BatteryKey));

            T? Get<T>(string key) => properties.TryGetValue(key, out var value) && value is T typed ? typed : default;
        }
    }
}
