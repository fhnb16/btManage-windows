using System.Runtime.InteropServices;

namespace BtManage.Bluetooth;

/// <summary>
/// Connects and disconnects the audio profiles of a Bluetooth device.
///
/// Windows has no public Bluetooth call for this. The audio driver has one: every Bluetooth
/// audio endpoint hangs off a kernel streaming filter that accepts the documented properties
/// KSPROPERTY_ONESHOT_RECONNECT and KSPROPERTY_ONESHOT_DISCONNECT. This class walks from the
/// audio endpoints to those filters and matches them to a device by the device node each
/// filter belongs to.
/// </summary>
internal static class AudioConnection
{
    private static readonly Guid BluetoothAudio = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    private const uint OneShotReconnect = 0;
    private const uint OneShotDisconnect = 1;
    private const uint PropertyTypeGet = 1;

    /// <summary>Instance identifier of the device node a kernel streaming filter belongs to.</summary>
    private static readonly PropertyKey DeviceInstanceId = new(new Guid("78C34FC8-104A-4ACA-9EA4-524D52996E57"), 256);

    private const int AllDataFlows = 2;
    private const int AllStates = 0x0F;
    private const int ClsCtxAll = 23;
    private const int StorageRead = 0;
    private const ushort VariantString = 31;

    /// <summary>Whether Windows has audio endpoints for the device, connected or not.</summary>
    /// <param name="nodes">Instance identifiers of the device's nodes; see <see cref="SystemDeviceSource.NodesOf"/>.</param>
    public static bool Supports(IReadOnlySet<string> nodes) => Filters(nodes).Count > 0;

    /// <summary>Asks the audio driver to connect the device's audio profiles.</summary>
    public static bool Connect(IReadOnlySet<string> nodes) => Send(nodes, OneShotReconnect);

    /// <summary>Asks the audio driver to drop the device's audio profiles.</summary>
    public static bool Disconnect(IReadOnlySet<string> nodes) => Send(nodes, OneShotDisconnect);

    private static bool Send(IReadOnlySet<string> nodes, uint property)
    {
        var sent = false;
        foreach (var control in Filters(nodes))
        {
            var request = new KsProperty { Set = BluetoothAudio, Id = property, Flags = PropertyTypeGet };
            sent |= control.KsProperty(ref request, (uint)Marshal.SizeOf<KsProperty>(), 0, 0, out _) >= 0;
        }

        return sent;
    }

    private static List<IKsControl> Filters(IReadOnlySet<string> nodes) =>
        [.. Enumerate().Where(filter => nodes.Contains(filter.Node)).Select(filter => filter.Control)];

    /// <summary>Every audio filter with its device node, once each: a device has one per profile.</summary>
    private static List<(string Node, IKsControl Control)> Enumerate()
    {
        var filters = new List<(string, IKsControl)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (enumerator.EnumAudioEndpoints(AllDataFlows, AllStates, out var endpoints) < 0
                || endpoints.GetCount(out var count) < 0)
            {
                return filters;
            }

            var topologyId = typeof(IDeviceTopology).GUID;
            var controlId = typeof(IKsControl).GUID;
            for (uint index = 0; index < count; index++)
            {
                // Endpoint → its topology → the first connector → the filter on the other side.
                if (endpoints.Item(index, out var endpoint) < 0
                    || endpoint.Activate(ref topologyId, ClsCtxAll, 0, out var topology) < 0
                    || ((IDeviceTopology)topology).GetConnector(0, out var connector) < 0
                    || connector.GetDeviceIdConnectedTo(out var filterId) < 0
                    || !seen.Add(filterId)
                    || enumerator.GetDevice(filterId, out var filter) < 0
                    || ReadString(filter, DeviceInstanceId) is not { Length: > 0 } node
                    || !node.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)
                    || filter.Activate(ref controlId, ClsCtxAll, 0, out var control) < 0)
                {
                    continue;
                }

                filters.Add((node, (IKsControl)control));
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
        {
            // No audio service, or an endpoint without a topology: nothing to control.
        }

        return filters;
    }

    private static string? ReadString(IMMDevice device, PropertyKey key)
    {
        if (device.OpenPropertyStore(StorageRead, out var store) < 0 || store.GetValue(ref key, out var value) < 0)
        {
            return null;
        }

        try
        {
            return value.Type == VariantString ? Marshal.PtrToStringUni(value.Pointer) : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid format, uint id)
    {
        public Guid Format = format;
        public uint Id = id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public nint Pointer;
        public nint Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KsProperty
    {
        public Guid Set;
        public uint Id;
        public uint Flags;
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator;

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int Item(uint index, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid interfaceId, int context, nint activationParameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

        [PreserveSig]
        int OpenPropertyStore(int access, out IPropertyStore store);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out int state);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int GetAt(uint index, out PropertyKey key);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport]
    [Guid("2A07407E-6497-4A18-9787-32F79BD0D98F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceTopology
    {
        [PreserveSig]
        int GetConnectorCount(out uint count);

        [PreserveSig]
        int GetConnector(uint index, out IConnector connector);
    }

    [ComImport]
    [Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IConnector
    {
        [PreserveSig]
        int GetType(out int type);

        [PreserveSig]
        int GetDataFlow(out int flow);

        [PreserveSig]
        int ConnectTo(IConnector connector);

        [PreserveSig]
        int Disconnect();

        [PreserveSig]
        int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);

        [PreserveSig]
        int GetConnectedTo(out IConnector connector);

        [PreserveSig]
        int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport]
    [Guid("28F54685-06FD-11D2-B27A-00A0C9223196")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IKsControl
    {
        [PreserveSig]
        int KsProperty(ref KsProperty property, uint propertyLength, nint data, uint dataLength, out uint bytesReturned);
    }
}
