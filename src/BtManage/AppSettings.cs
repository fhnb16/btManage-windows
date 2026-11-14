using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace BtManage;

/// <summary>Where the application keeps its files: %LOCALAPPDATA%\btManage.</summary>
internal static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "btManage");

    public static string Settings => Path.Combine(DataDirectory, "settings.json");

    /// <summary>Settings of demo mode, so that trying things out leaves the real ones alone.</summary>
    public static string DemoSettings => Path.Combine(DataDirectory, "settings.demo.json");

    /// <summary>Remembered battery levels and their history, written by the core.</summary>
    public static string State => Path.Combine(DataDirectory, "state.json");
}

/// <summary>User preferences, stored as JSON.</summary>
internal sealed class AppSettings
{
    private string? _path;

    public bool NotifyOnArrival { get; set; } = true;

    public bool NotifyOnLow { get; set; } = true;

    /// <summary>At or below this level, and not charging, a battery counts as low.</summary>
    public int LowThreshold { get; set; } = 20;

    /// <summary>
    /// Ask PlayStation controllers for their full input report, which carries the battery.
    /// Off by default: it changes what the controller sends to every program.
    /// </summary>
    public bool PlayStationFullReports { get; set; }

    /// <summary>
    /// The look of the application: "fluent" for the look of Windows, "material" for
    /// Material Design. Applied at once.
    /// </summary>
    public string Design { get; set; } = "fluent";

    /// <summary>"system", "light" or "dark". Applied at once.</summary>
    public string Theme { get; set; } = "system";

    /// <summary>
    /// The material behind the windows, in either design: "auto" (Mica for the window,
    /// Acrylic for the tray panel), "acrylic", "mica", "mica_alt", or "solid" for none.
    /// </summary>
    public string Backdrop { get; set; } = "auto";

    /// <summary>"system" for the accent colour of Windows, "bluetooth" for Bluetooth blue. Applied at once.</summary>
    public string Accent { get; set; } = "system";

    /// <summary>"system", "en" or "ru". Applied at start.</summary>
    public string Language { get; set; } = "system";

    /// <summary>Look for a newer release once a day.</summary>
    public bool CheckUpdates { get; set; } = true;

    /// <summary>The newest release the user has been told about, so each is announced once.</summary>
    public string? AnnouncedVersion { get; set; }

    /// <summary>Devices the user wants no notifications about.</summary>
    public HashSet<string> MutedDevices { get; set; } = [];

    /// <summary>Take files and text other btManage applications on the local network send.</summary>
    public bool ShareReceive { get; set; }

    /// <summary>Identifier of this installation among the others on the network; made at the first start.</summary>
    public string? ShareId { get; set; }

    /// <summary>The devices paired for sharing: their identifiers.</summary>
    public List<string> ShareTrusted { get; set; } = [];

    /// <summary>Raised after <see cref="Save"/>.</summary>
    public event Action? Changed;

    /// <summary>Reads the settings, or returns the defaults when there are none yet.</summary>
    /// <param name="path">Null keeps the settings in memory only, for demo mode.</param>
    public static AppSettings Load(string? path)
    {
        AppSettings? settings = null;
        try
        {
            if (path is not null && File.Exists(path))
            {
                settings = JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJson.Default.AppSettings);
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged file is replaced by the defaults on the next save.
        }

        settings ??= new AppSettings();
        settings._path = path;
        return settings;
    }

    public void Save()
    {
        if (_path is not null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(this, SettingsJson.Default.AppSettings));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The settings still apply for this run.
            }
        }

        Changed?.Invoke();
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;

/// <summary>Starting with Windows, through the current user's Run key.</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "btManage";

    /// <summary>Switch that keeps the window closed at start; see <see cref="Options"/>.</summary>
    public const string TraySwitch = "--tray";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } executable)
        {
            key.SetValue(ValueName, $"\"{executable}\" {TraySwitch}");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
