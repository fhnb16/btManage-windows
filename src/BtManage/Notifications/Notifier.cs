using BtManage.Interop;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace BtManage.Notifications;

/// <summary>
/// Shows a toast when headphones connect or come near, and when a battery of any device runs
/// low. Clicking a toast raises <see cref="Opened"/> with the device it is about.
/// </summary>
internal sealed class Notifier : IDisposable
{
    private const string DeviceArgument = "device";

    private readonly AppSettings _settings;
    private readonly Dictionary<string, Seen> _seen = [];
    private readonly bool _registered;

    /// <summary>The first list only sets the baseline: nothing in it is news.</summary>
    private bool _primed;

    public Notifier(AppSettings settings)
    {
        _settings = settings;
        try
        {
            // The handler must be in place before Register, or a click starts a second process.
            AppNotificationManager.Default.NotificationInvoked += OnInvoked;
            var icon = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
            AppNotificationManager.Default.Register(Strings.Get("AppName"), icon);
            _registered = true;
        }
        catch (Exception)
        {
            // Notifications are unavailable, for example in an elevated process. The tray icon
            // and the windows still work.
        }
    }

    /// <summary>A toast was clicked. Raised on a background thread, with the device identifier.</summary>
    public event Action<string?>? Opened;

    public void Update(Snapshot snapshot)
    {
        foreach (var device in snapshot.Devices)
        {
            var active = device.IsActive;
            var low = LowCells(device);
            _seen.TryGetValue(device.Id, out var before);
            _seen[device.Id] = new Seen(active, low.Mask);

            // Only the user's own devices are news: somebody else's earbuds walking past are not.
            if (!_primed || !active || !device.Paired || _settings.MutedDevices.Contains(device.Id))
            {
                continue;
            }

            if (!before.Active && IsAudio(device) && device.Cells.Count > 0)
            {
                if (_settings.NotifyOnArrival)
                {
                    Show(device, DeviceText.Levels(device));
                }
            }
            else if ((low.Mask & ~before.LowMask) != 0 && _settings.NotifyOnLow)
            {
                // Another battery dropped to the threshold. Further drops of the same one stay quiet.
                Show(device, Strings.Format("ToastLow", low.Text));
            }
        }

        foreach (var id in _seen.Keys.Except(snapshot.Devices.Select(device => device.Id)).ToList())
        {
            _seen.Remove(id);
        }

        _primed = true;
    }

    /// <summary>Shows a toast that is not about a device. A click reports <paramref name="target"/>.</summary>
    public void Announce(string title, string body, string target) => Show(title, body, target);

    public void Dispose()
    {
        if (_registered)
        {
            AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
            AppNotificationManager.Default.Unregister();
        }
    }

    private static bool IsAudio(Device device) =>
        device.Kind is Kind.Earbuds or Kind.Headphones or Kind.Headset or Kind.Speaker;

    /// <summary>The batteries that are low and current: one bit each, and the same as text.</summary>
    private (int Mask, string Text) LowCells(Device device)
    {
        var mask = 0;
        var parts = new List<string>(3);
        foreach (var cell in device.Cells.Where(cell => cell.IsFresh && cell.IsLow(_settings.LowThreshold)))
        {
            mask |= 1 << (int)cell.Role;
            parts.Add(device.HasEarbudCells
                ? $"{DeviceText.RoleLabel(cell.Role)} {DeviceText.Percent(cell.Percent)}"
                : DeviceText.Percent(cell.Percent));
        }

        return (mask, string.Join(", ", parts));
    }

    private void Show(Device device, string body) => Show(DeviceText.Name(device), body, device.Id);

    private void Show(string title, string body, string target)
    {
        if (!_registered)
        {
            return;
        }

        var builder = new AppNotificationBuilder()
            .AddArgument(DeviceArgument, target)
            .AddText(title);
        if (body.Length > 0)
        {
            builder.AddText(body);
        }

        try
        {
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception)
        {
            // Notifications can be switched off by policy; nothing to do about it here.
        }
    }

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) =>
        Opened?.Invoke(args.Arguments.TryGetValue(DeviceArgument, out var device) ? device : null);

    private readonly record struct Seen(bool Active, int LowMask);
}
