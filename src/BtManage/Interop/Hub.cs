using System.Runtime.InteropServices;
using System.Text.Json;

namespace BtManage.Interop;

/// <summary>
/// The device hub of the Rust core (btmanage_ffi.dll): one list of devices from every source,
/// with remembered levels and their history. Safe to call from any thread. Times are Unix
/// milliseconds, taken here.
/// </summary>
internal sealed partial class Hub : SafeHandle
{
    private const string Library = "btmanage_ffi";

    public const byte ManufacturerData = 0;
    public const byte ServiceData = 1;

    public Hub() : base(invalidHandleValue: 0, ownsHandle: true)
    {
        SetHandle(btm_hub_new());
    }

    public override bool IsInvalid => handle == 0;

    /// <summary>Version of the core library.</summary>
    public static string CoreVersion => Marshal.PtrToStringUTF8(btm_version()) ?? string.Empty;

    private static ulong Now => (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Feeds one advertisement. Returns the list when what the user sees has changed.</summary>
    /// <param name="kind"><see cref="ManufacturerData"/> or <see cref="ServiceData"/>.</param>
    /// <param name="id">Company identifier, or 16-bit service UUID.</param>
    /// <param name="data">The payload after that identifier.</param>
    public unsafe Snapshot? Advertisement(ulong address, short rssi, byte kind, ushort id, ReadOnlySpan<byte> data)
    {
        var added = false;
        try
        {
            DangerousAddRef(ref added);
            fixed (byte* bytes = data)
            {
                return Parse(Take(btm_hub_advertisement(handle, address, rssi, kind, id, bytes, (nuint)data.Length, Now)));
            }
        }
        finally
        {
            if (added)
            {
                DangerousRelease();
            }
        }
    }

    /// <summary>Replaces the list of devices paired with Windows.</summary>
    public Snapshot? SetSystemDevices(IReadOnlyList<SystemDeviceInfo> devices)
    {
        var json = JsonSerializer.Serialize(devices, CoreJson.Default.IReadOnlyListSystemDeviceInfo);
        return Call(hub => btm_hub_set_system_devices(hub, json, Now));
    }

    /// <summary>Records a battery level read directly from a device.</summary>
    public Snapshot? Battery(string deviceId, Source source, int percent, bool charging)
    {
        var code = source switch
        {
            Source.Gatt => (byte)2,
            Source.Hid => (byte)3,
            Source.Gamepad => (byte)4,
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };
        return Call(hub => btm_hub_battery(hub, deviceId, code, (byte)Math.Clamp(percent, 0, 255), charging, Now));
    }

    /// <summary>Feeds a HID input report, report identifier first.</summary>
    public unsafe Snapshot? HidReport(string deviceId, ushort vendorId, ushort productId, ReadOnlySpan<byte> report)
    {
        var added = false;
        try
        {
            DangerousAddRef(ref added);
            fixed (byte* bytes = report)
            {
                return Parse(Take(btm_hub_hid_report(handle, deviceId, vendorId, productId, bytes, (nuint)report.Length, Now)));
            }
        }
        finally
        {
            if (added)
            {
                DangerousRelease();
            }
        }
    }

    /// <summary>The current list. Also lets the core forget what has grown too old.</summary>
    public Snapshot Current() => Call(hub => btm_hub_snapshot(hub, Now)) ?? Snapshot.Empty;

    /// <summary>The recorded levels of a device over the last <paramref name="window"/>.</summary>
    public IReadOnlyList<RoleHistory> History(string deviceId, TimeSpan window)
    {
        var since = Now - (ulong)window.TotalMilliseconds;
        var json = Text(hub => btm_hub_history(hub, deviceId, since));
        return json is null ? [] : JsonSerializer.Deserialize(json, CoreJson.Default.IReadOnlyListRoleHistory) ?? [];
    }

    public void Forget(string deviceId) => Use(hub => btm_hub_forget(hub, deviceId));

    /// <summary>What the hub remembers, as JSON to keep between runs.</summary>
    public string? Export() => Text(btm_hub_export);

    /// <summary>Brings back what <see cref="Export"/> returned in an earlier run.</summary>
    public bool Import(string state)
    {
        var imported = false;
        Use(hub => imported = btm_hub_import(hub, state));
        return imported;
    }

    /// <summary>
    /// Reads the JSON of a GitHub release and returns its description as JSON if it is
    /// newer than <paramref name="current"/>, otherwise null.
    /// </summary>
    /// <param name="assetHint">Part of the name of the file to download.</param>
    public static string? UpdateCheck(string current, string releaseJson, string assetHint) =>
        Take(btm_update_check(current, releaseJson, assetHint));

    protected override bool ReleaseHandle()
    {
        btm_hub_free(handle);
        return true;
    }

    private delegate nint NativeCall(nint hub);

    private Snapshot? Call(NativeCall call) => Parse(Text(call));

    private static Snapshot? Parse(string? json) =>
        json is null ? null : JsonSerializer.Deserialize(json, CoreJson.Default.Snapshot);

    /// <summary>Runs a call that returns a string owned by the core, and frees the string.</summary>
    private string? Text(NativeCall call)
    {
        nint result = 0;
        Use(hub => result = call(hub));
        return Take(result);
    }

    /// <summary>Copies a string owned by the core and frees it.</summary>
    private static string? Take(nint text)
    {
        if (text == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(text);
        }
        finally
        {
            btm_string_free(text);
        }
    }

    /// <summary>Keeps the handle alive for the duration of a call.</summary>
    private void Use(Action<nint> action)
    {
        var added = false;
        try
        {
            DangerousAddRef(ref added);
            action(handle);
        }
        finally
        {
            if (added)
            {
                DangerousRelease();
            }
        }
    }

    [LibraryImport(Library)]
    private static partial nint btm_version();

    [LibraryImport(Library)]
    private static partial void btm_string_free(nint text);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint btm_update_check(string current, string releaseJson, string assetHint);

    [LibraryImport(Library)]
    private static partial nint btm_hub_new();

    [LibraryImport(Library)]
    private static partial void btm_hub_free(nint hub);

    [LibraryImport(Library)]
    private static unsafe partial nint btm_hub_advertisement(
        nint hub, ulong address, short rssi, byte kind, ushort id, byte* data, nuint length, ulong nowMs);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint btm_hub_set_system_devices(nint hub, string devices, ulong nowMs);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint btm_hub_battery(
        nint hub, string deviceId, byte source, byte percent, [MarshalAs(UnmanagedType.U1)] bool charging, ulong nowMs);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static unsafe partial nint btm_hub_hid_report(
        nint hub, string deviceId, ushort vendorId, ushort productId, byte* data, nuint length, ulong nowMs);

    [LibraryImport(Library)]
    private static partial nint btm_hub_snapshot(nint hub, ulong nowMs);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint btm_hub_history(nint hub, string deviceId, ulong sinceMs);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial void btm_hub_forget(nint hub, string deviceId);

    [LibraryImport(Library)]
    private static partial nint btm_hub_export(nint hub);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool btm_hub_import(nint hub, string state);
}
