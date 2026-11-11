using BtManage.Interop;

namespace BtManage;

/// <summary>Wording and glyphs shared by the flyout, the window, the tray and the notifications.</summary>
internal static class DeviceText
{
    public static string Name(Device device)
    {
        if (device.Name.Length > 0)
        {
            return device.Name;
        }

        // An unpaired device has no name of its own; say what it is.
        return device.Model ?? Strings.Get(device.Origin == Origin.FastPair ? "UnknownFastPair" : "UnknownApple");
    }

    public static string KindName(Kind kind) => Strings.Get($"Kind{kind}");

    /// <summary>The glyph for a kind of device in the system icon font.</summary>
    public static string Glyph(Kind kind) => kind switch
    {
        Kind.Earbuds or Kind.Headphones => "",
        Kind.Headset => "",
        Kind.Speaker => "",
        Kind.Mouse => "",
        Kind.Keyboard => "",
        Kind.Gamepad => "",
        Kind.Remote => "",
        Kind.Phone => "",
        Kind.Tablet => "",
        Kind.Computer => "",
        Kind.Car => "",
        _ => "",
    };

    public static string Status(Device device) => Strings.Get(
        device.Connected ? "StatusConnected" : device.Nearby ? "StatusNearby" : "StatusDisconnected");

    /// <summary>"Headphones · Connected".</summary>
    public static string Summary(Device device) => $"{KindName(device.Kind)} · {Status(device)}";

    public static string RoleLabel(Role role) => Strings.Get($"Role{role}");

    public static string Percent(int percent) => Strings.Format("PercentFormat", percent);

    /// <summary>"Left 90 %, Right 70 %, Case 50 %", or the single level of other devices.</summary>
    public static string Levels(Device device)
    {
        if (!device.HasEarbudCells)
        {
            return device.Cell(Role.Main) is { } main ? Percent(main.Percent) : string.Empty;
        }

        return string.Join(", ", device.Cells.Select(cell => $"{RoleLabel(cell.Role)} {Percent(cell.Percent)}"));
    }

    /// <summary>The one level worth showing in a list row: the lowest, the case aside.</summary>
    public static string ShortLevel(Device device) =>
        device.LowestCell is { } cell ? Percent(cell.Percent) : string.Empty;

    /// <summary>How long ago, or an empty string for a reading that is current.</summary>
    public static string Age(long ageMs) => ageMs switch
    {
        < Cell.FreshMs => string.Empty,
        < 3_600_000 => Strings.Format("AgeMinutes", ageMs / 60_000),
        < 48 * 3_600_000L => Strings.Format("AgeHours", ageMs / 3_600_000),
        _ => Strings.Format("AgeDays", ageMs / (24 * 3_600_000L)),
    };

    /// <summary>"About 3 h 20 min left".</summary>
    public static string Estimate(int minutes)
    {
        var hours = minutes / 60;
        var rest = minutes % 60 / 5 * 5;
        if (hours == 0)
        {
            return Strings.Format("EstimateMinutes", Math.Max(minutes, 1));
        }

        // Past a few hours the minutes are false precision.
        return hours >= 5 || rest == 0
            ? Strings.Format("EstimateHours", hours)
            : Strings.Format("EstimateHoursMinutes", hours, rest);
    }

    /// <summary>Tooltip of the tray icon: the active devices that report a level.</summary>
    public static string Tooltip(Snapshot snapshot)
    {
        var lines = snapshot.Devices
            .Where(device => device.IsActive && device.Cells.Count > 0)
            .Take(3)
            .Select(device => $"{Name(device)}: {Levels(device)}")
            .ToList();
        return lines.Count == 0 ? Strings.Get("AppName") : string.Join("\n", lines);
    }
}
