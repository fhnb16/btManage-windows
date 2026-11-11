using System.Runtime.InteropServices;
using System.Text.Json;

namespace BtManage.Interop;

/// <summary>
/// The device tracker of the Rust core (btmanage_ffi.dll). Safe to call from any thread.
/// </summary>
internal sealed partial class Tracker : SafeHandle
{
    private const string Library = "btmanage_ffi";

    public Tracker() : base(invalidHandleValue: 0, ownsHandle: true)
    {
        SetHandle(btm_tracker_new());
    }

    public override bool IsInvalid => handle == 0;

    /// <summary>Version of the core library.</summary>
    public static string CoreVersion => Marshal.PtrToStringUTF8(btm_version()) ?? string.Empty;

    /// <summary>
    /// Feeds one advertisement. Returns the new state when what the user sees has changed.
    /// </summary>
    /// <param name="data">Manufacturer data that follows the company identifier.</param>
    /// <param name="nowMs">Monotonic clock reading in milliseconds.</param>
    public unsafe Snapshot? Ingest(ulong address, short rssi, ushort companyId, ReadOnlySpan<byte> data, long nowMs)
    {
        var added = false;
        try
        {
            DangerousAddRef(ref added);
            fixed (byte* bytes = data)
            {
                return Take(btm_tracker_ingest(handle, address, rssi, companyId, bytes, (nuint)data.Length, (ulong)nowMs));
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

    /// <summary>The current state. Also lets the core drop devices that have gone silent.</summary>
    public Snapshot Current(long nowMs)
    {
        var added = false;
        try
        {
            DangerousAddRef(ref added);
            return Take(btm_tracker_snapshot(handle, (ulong)nowMs)) ?? Snapshot.Empty;
        }
        finally
        {
            if (added)
            {
                DangerousRelease();
            }
        }
    }

    protected override bool ReleaseHandle()
    {
        btm_tracker_free(handle);
        return true;
    }

    private static Snapshot? Take(nint json)
    {
        if (json == 0)
        {
            return null;
        }

        try
        {
            var text = Marshal.PtrToStringUTF8(json);
            return text is null ? null : JsonSerializer.Deserialize(text, SnapshotJson.Default.Snapshot);
        }
        finally
        {
            btm_string_free(json);
        }
    }

    [LibraryImport(Library)]
    private static partial nint btm_version();

    [LibraryImport(Library)]
    private static partial nint btm_tracker_new();

    [LibraryImport(Library)]
    private static partial void btm_tracker_free(nint tracker);

    [LibraryImport(Library)]
    private static unsafe partial nint btm_tracker_ingest(
        nint tracker, ulong address, short rssi, ushort companyId, byte* data, nuint length, ulong nowMs);

    [LibraryImport(Library)]
    private static partial nint btm_tracker_snapshot(nint tracker, ulong nowMs);

    [LibraryImport(Library)]
    private static partial void btm_string_free(nint text);
}
