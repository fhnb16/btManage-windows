using BtManage.Interop;

namespace BtManage;

/// <summary>Wording shared by the flyout, the tray tooltip and the notifications.</summary>
internal static class DeviceText
{
    /// <summary>A reading older than this is shown with its age.</summary>
    private const long FreshMs = 30_000;

    public static string Name(Device device) => device.Model ?? Strings.Get("UnknownModel");

    public static string Percent(int percent) => Strings.Format("PercentFormat", percent);

    /// <summary>"Left 90 %, Right 70 %, Case 50 %", or the single level of headphones.</summary>
    public static string Levels(Device device)
    {
        if (device.IsHeadphones)
        {
            return device.Main is { } main ? Percent(main.Percent) : string.Empty;
        }

        var parts = new List<string>(3);
        Add(parts, "CellLeft", device.Left);
        Add(parts, "CellRight", device.Right);
        Add(parts, "CellCase", device.Case);
        return string.Join(", ", parts);

        static void Add(List<string> parts, string label, Cell? cell)
        {
            if (cell is not null)
            {
                parts.Add($"{Strings.Get(label)} {Percent(cell.Percent)}");
            }
        }
    }

    /// <summary>How long ago the device was heard, or an empty string while it is fresh.</summary>
    public static string Age(Device device) => device.AgeMs switch
    {
        < FreshMs => string.Empty,
        < 60_000 => Strings.Get("UpdatedUnderMinute"),
        _ => Strings.Format("UpdatedMinutes", device.AgeMs / 60_000),
    };

    /// <summary>Tooltip of the tray icon: the nearest device and its levels.</summary>
    public static string Tooltip(Snapshot snapshot)
    {
        if (snapshot.Devices is not [var nearest, ..])
        {
            return Strings.Get("AppName");
        }

        var levels = Levels(nearest);
        return levels.Length == 0 ? Name(nearest) : $"{Name(nearest)}\n{levels}";
    }
}
