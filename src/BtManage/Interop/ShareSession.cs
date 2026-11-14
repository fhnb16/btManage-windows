using System.Runtime.InteropServices;
using System.Text.Json;

namespace BtManage.Interop;

/// <summary>Another btManage on the local network.</summary>
internal sealed record SharePeer(string Id, string Name, string Platform, string Address);

/// <summary>
/// Something that happened in the sharing: an item received, sent, or failed. Which fields
/// are set depends on <see cref="Type"/>.
/// </summary>
internal sealed record ShareEvent(
    string Type,
    string? Kind,
    string? From,
    string? To,
    string? Name,
    long Size,
    string? Path,
    string? Text,
    string? Error);

/// <summary>
/// Files and text between btManage applications on one local network, through the core
/// (<c>btm_share_*</c>): announces this computer, hears the others, takes items while the
/// user lets it. Safe to call from any thread.
/// </summary>
internal sealed partial class ShareSession : SafeHandle
{
    private const string Library = "btmanage_ffi";

    private ShareSession(nint session) : base(invalidHandleValue: 0, ownsHandle: true)
    {
        SetHandle(session);
    }

    public override bool IsInvalid => handle == 0;

    /// <summary>Starts taking part; null when it could not, as without a network.</summary>
    public static ShareSession? Start(string id, string name, string inbox)
    {
        var session = btm_share_start(id, name, "windows", inbox, discovery: true);
        return session == 0 ? null : new ShareSession(session);
    }

    public ushort Port => Use(btm_share_port);

    public void SetReceiving(bool on) => Use(session =>
    {
        btm_share_set_receiving(session, on);
        return 0;
    });

    public IReadOnlyList<SharePeer> Peers()
    {
        var json = Take(Use(btm_share_peers));
        return json is null ? [] : JsonSerializer.Deserialize(json, CoreJson.Default.IReadOnlyListSharePeer) ?? [];
    }

    /// <summary>What happened since the last call.</summary>
    public IReadOnlyList<ShareEvent> Events()
    {
        var json = Take(Use(btm_share_events));
        return json is null ? [] : JsonSerializer.Deserialize(json, CoreJson.Default.IReadOnlyListShareEvent) ?? [];
    }

    /// <param name="to">The address of the other side, "ip:port".</param>
    public void SendText(string to, string text) => Use(session => btm_share_send_text(session, to, text) ? 1 : 0);

    /// <param name="to">The address of the other side, "ip:port".</param>
    public void SendFile(string to, string path) => Use(session => btm_share_send_file(session, to, path) ? 1 : 0);

    protected override bool ReleaseHandle()
    {
        btm_share_free(handle);
        return true;
    }

    /// <summary>Keeps the handle alive for the duration of a call.</summary>
    private T Use<T>(Func<nint, T> call)
    {
        var added = false;
        try
        {
            DangerousAddRef(ref added);
            return call(handle);
        }
        finally
        {
            if (added)
            {
                DangerousRelease();
            }
        }
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

    [LibraryImport(Library)]
    private static partial void btm_string_free(nint text);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint btm_share_start(
        string id, string name, string platform, string inbox, [MarshalAs(UnmanagedType.U1)] bool discovery);

    [LibraryImport(Library)]
    private static partial void btm_share_free(nint session);

    [LibraryImport(Library)]
    private static partial ushort btm_share_port(nint session);

    [LibraryImport(Library)]
    private static partial void btm_share_set_receiving(nint session, [MarshalAs(UnmanagedType.U1)] bool on);

    [LibraryImport(Library)]
    private static partial nint btm_share_peers(nint session);

    [LibraryImport(Library)]
    private static partial nint btm_share_events(nint session);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool btm_share_send_text(nint session, string to, string text);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool btm_share_send_file(nint session, string to, string path);
}
