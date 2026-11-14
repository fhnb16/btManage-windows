using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using BtManage.Interop;
using Microsoft.UI.Dispatching;

namespace BtManage;

/// <summary>
/// Files and text between this computer and other devices with btManage on the same
/// network. Items come only from paired devices, and only while the user lets them; they
/// land in Downloads\btManage. The paired devices are kept in the settings.
/// </summary>
internal sealed class Sharing : IDisposable
{
    private readonly AppSettings _settings;
    private readonly DispatcherQueueTimer _timer;
    private readonly List<ShareEvent> _history = [];
    private ShareSession? _session;

    /// <summary>Where received files are stored.</summary>
    public static string Inbox { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "btManage");

    public Sharing(DispatcherQueue queue, AppSettings settings)
    {
        _settings = settings;
        Name = Environment.MachineName;
        try
        {
            _settings.ShareId ??= Guid.NewGuid().ToString();
            _settings.Save();
            _session = ShareSession.Start(_settings.ShareId, Name, Inbox);
            if (_session is { } session)
            {
                foreach (var id in _settings.ShareTrusted)
                {
                    session.Trust(id);
                }

                session.SetReceiving(_settings.ShareReceive);
                Code = session.Code;
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // A core library older than the sharing: the page says sharing is not running.
            _session = null;
        }

        _settings.Changed += () => _session?.SetReceiving(_settings.ShareReceive);
        _timer = queue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    /// <summary>The name other devices see.</summary>
    public string Name { get; }

    public bool Running => _session is not null;

    /// <summary>The code another device types in to pair with this one; it changes after too many wrong tries.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The other devices heard recently.</summary>
    public IReadOnlyList<SharePeer> Peers { get; private set; } = [];

    /// <summary>What was sent and received, newest first.</summary>
    public IReadOnlyList<ShareEvent> History => _history;

    /// <summary>Raised on the UI thread when the peers, the code or the history changed.</summary>
    public event Action? Changed;

    /// <summary>Raised on the UI thread for each item that came in.</summary>
    public event Action<ShareEvent>? Received;

    /// <summary>Where other devices can reach this computer by hand: "ip:port" per address.</summary>
    public IReadOnlyList<string> Addresses()
    {
        if (_session is not { } session)
        {
            return [];
        }

        var port = session.Port;
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Select(unicast => unicast.Address)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                .Select(address => $"{address}:{port}")
                .Distinct()
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    public void SendText(string to, string text) => _session?.SendText(to.Trim(), text);

    public void SendFile(string to, string path) => _session?.SendFile(to.Trim(), path);

    public void Pair(string to, string code) => _session?.Pair(to.Trim(), code);

    /// <summary>Stops taking items from a device, until it is paired again.</summary>
    public void Forget(string id)
    {
        _session?.Forget(id);
        KeepTrusted();
        Poll();
    }

    private void Poll()
    {
        if (_session is not { } session)
        {
            return;
        }

        var peers = session.Peers();
        var code = session.Code;
        var events = session.Events();
        var changed = !peers.SequenceEqual(Peers) || code != Code || events.Count > 0;
        Peers = peers;
        Code = code;
        foreach (var item in events)
        {
            _history.Insert(0, item);
            if (item.Type == "received")
            {
                Received?.Invoke(item);
            }
        }

        if (events.Any(item => item.Type == "paired"))
        {
            KeepTrusted();
        }

        if (_history.Count > MaxHistory)
        {
            _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Writes the paired devices into the settings, for the next start.</summary>
    private void KeepTrusted()
    {
        if (_session is { } session)
        {
            _settings.ShareTrusted = [.. session.Trusted()];
            _settings.Save();
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _session?.Dispose();
        _session = null;
    }

    private const int MaxHistory = 100;
}
