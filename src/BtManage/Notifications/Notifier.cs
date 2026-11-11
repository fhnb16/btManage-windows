using BtManage.Interop;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace BtManage.Notifications;

/// <summary>
/// Shows a toast when a device comes into range and when one of its batteries runs low.
/// </summary>
internal sealed class Notifier : IDisposable
{
    /// <summary>A device silent for this long counts as gone; hearing it again is an arrival.</summary>
    private const long GoneMs = 60_000;

    private readonly Dictionary<string, Seen> _seen = [];
    private readonly bool _registered;

    public Notifier()
    {
        try
        {
            // Name and icon shown in the header of the toast.
            var icon = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
            AppNotificationManager.Default.Register(Strings.Get("AppName"), icon);
            _registered = true;
        }
        catch (Exception)
        {
            // Notifications are unavailable, for example in an elevated process. The tray icon
            // and the flyout still work.
        }
    }

    public void Update(Snapshot snapshot)
    {
        foreach (var device in snapshot.Devices)
        {
            var present = device.AgeMs < GoneMs;
            var low = LowCells(device);
            _seen.TryGetValue(device.Id, out var before);

            if (present && !before.Present)
            {
                Show(DeviceText.Name(device), DeviceText.Levels(device));
            }
            else if (present && (low.Mask & ~before.LowMask) != 0)
            {
                // Another battery dropped to the threshold. Further drops of the same one stay quiet.
                Show(DeviceText.Name(device), Strings.Format("ToastLow", low.Text));
            }

            _seen[device.Id] = new Seen(present, low.Mask);
        }

        foreach (var id in _seen.Keys.Except(snapshot.Devices.Select(device => device.Id)).ToList())
        {
            _seen.Remove(id);
        }
    }

    public void Dispose()
    {
        if (_registered)
        {
            AppNotificationManager.Default.Unregister();
        }
    }

    /// <summary>The batteries that are low: one bit each, and the same as text.</summary>
    private static (int Mask, string Text) LowCells(Device device)
    {
        var mask = 0;
        var parts = new List<string>(3);
        Add(1, "CellLeft", device.Left);
        Add(2, "CellRight", device.Right);
        Add(4, "CellCase", device.Case);
        Add(8, "CellMain", device.Main);
        return (mask, string.Join(", ", parts));

        void Add(int bit, string label, Cell? cell)
        {
            if (cell is { IsLow: true })
            {
                mask |= bit;
                parts.Add($"{Strings.Get(label)} {DeviceText.Percent(cell.Percent)}");
            }
        }
    }

    private void Show(string title, string body)
    {
        if (!_registered)
        {
            return;
        }

        var builder = new AppNotificationBuilder().AddText(title);
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

    private readonly record struct Seen(bool Present, int LowMask);
}
