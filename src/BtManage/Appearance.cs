using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace BtManage;

/// <summary>
/// Theme, design, accent, language and window material, as chosen in the settings. All but
/// the language apply while the application runs: <see cref="Apply"/> sets the resources of
/// the application, <see cref="ApplyWindow"/> brings a window up to date with them.
/// </summary>
internal static class Appearance
{
    /// <summary>Makes the second process of a restart wait for the first to go; see <see cref="Restart"/>.</summary>
    public const string RestartedSwitch = "--restarted";

    /// <summary>
    /// Applies the language. Call before the first string is loaded: strings already on
    /// screen do not change, which is why a new choice restarts the application.
    /// </summary>
    public static void ApplyLanguage(AppSettings settings)
    {
        var tag = settings.Language switch
        {
            "en" => "en-US",
            "ru" => "ru-RU",
            _ => null,
        };
        if (tag is null)
        {
            return;
        }

        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = tag;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(tag);
        }
        catch (Exception)
        {
            // Stay with the system language.
        }
    }

    /// <summary>The theme of the windows' content; <c>Default</c> follows Windows.</summary>
    public static ElementTheme Theme(AppSettings settings) => settings.Theme switch
    {
        "light" => ElementTheme.Light,
        "dark" => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    public static bool IsMaterial(AppSettings settings) => settings.Design == "material";

    /// <summary>Whether the windows have a solid background instead of Mica or Acrylic.</summary>
    public static bool IsSolid(AppSettings settings) =>
        settings.Backdrop == "solid" || !DesktopAcrylicController.IsSupported();

    /// <summary>The blue of the Bluetooth mark.</summary>
    private static readonly Windows.UI.Color BluetoothBlue = Windows.UI.Color.FromArgb(255, 0x00, 0x82, 0xFC);

    private static readonly string[] AccentKeys =
    [
        "SystemAccentColor",
        "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
        "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
    ];

    /// <summary>Brushes the Material design replaces, per theme.</summary>
    private static readonly string[] MaterialKeys =
    [
        "ApplicationPageBackgroundThemeBrush",
        "LayerFillColorDefaultBrush",
        "CardBackgroundFillColorDefaultBrush",
        "CardStrokeColorDefaultBrush",
    ];

    /// <summary>The brush behind the content of every window; see App.xaml.</summary>
    private const string WindowBackgroundKey = "BtmWindowBackgroundBrush";

    /// <summary>What the resources were last set for, so that a save of another setting leaves them alone.</summary>
    private static string? _applied;

    /// <summary>What each window was last brought up to date with.</summary>
    private static readonly ConditionalWeakTable<Window, string> WindowsApplied = new();

    private static string Signature(AppSettings settings) =>
        $"{settings.Design}|{settings.Theme}|{settings.Accent}|{settings.Backdrop}";

    /// <summary>
    /// Sets the resources of the application for the accent, the design and the backdrop
    /// chosen. Windows that exist take the change with <see cref="ApplyWindow"/>.
    /// </summary>
    public static void Apply(Application application, AppSettings settings)
    {
        var signature = Signature(settings);
        if (signature == _applied)
        {
            return;
        }

        _applied = signature;
        ApplyAccent(application, settings);
        ApplyDesign(application, settings);
    }

    /// <summary>Replaces the accent colour of Windows with Bluetooth blue, or gives it back.</summary>
    private static void ApplyAccent(Application application, AppSettings settings)
    {
        var resources = application.Resources;
        if (settings.Accent != "bluetooth")
        {
            foreach (var key in AccentKeys)
            {
                resources.Remove(key);
            }

            return;
        }

        resources["SystemAccentColor"] = BluetoothBlue;
        // The lighter shades are used on dark surfaces, the darker ones on light surfaces.
        resources["SystemAccentColorLight1"] = Mix(BluetoothBlue, 255, 0.18);
        resources["SystemAccentColorLight2"] = Mix(BluetoothBlue, 255, 0.36);
        resources["SystemAccentColorLight3"] = Mix(BluetoothBlue, 255, 0.54);
        resources["SystemAccentColorDark1"] = Mix(BluetoothBlue, 0, 0.16);
        resources["SystemAccentColorDark2"] = Mix(BluetoothBlue, 0, 0.32);
        resources["SystemAccentColorDark3"] = Mix(BluetoothBlue, 0, 0.48);
    }

    /// <summary>
    /// The surfaces of the design chosen. Fluent keeps the brushes of Windows. Material
    /// Design has surfaces in neutral tones tinted with the accent, cards without a border
    /// and rounder corners; over Mica or Acrylic its surfaces let the material through.
    /// The controls stay the ones of Windows in either design.
    /// </summary>
    private static void ApplyDesign(Application application, AppSettings settings)
    {
        var resources = application.Resources;
        var material = IsMaterial(settings);
        var solid = IsSolid(settings);

        foreach (var dark in new[] { false, true })
        {
            var theme = ThemeDictionary(resources, dark ? "Dark" : "Light");
            foreach (var key in MaterialKeys)
            {
                theme.Remove(key);
            }

            if (material)
            {
                var accent = Accent(settings);
                Windows.UI.Color Tone(byte grey, double share, byte alpha)
                {
                    var tone = Blend(Windows.UI.Color.FromArgb(255, grey, grey, grey), accent, share);
                    return Windows.UI.Color.FromArgb(alpha, tone.R, tone.G, tone.B);
                }

                // The window lightest (or darkest), the content pane a step off it, cards
                // another step. Over a backdrop each lets some of it through.
                byte surfaceAlpha = solid ? (byte)255 : (byte)0x99;
                byte paneAlpha = solid ? (byte)255 : (byte)0xB3;
                byte cardAlpha = solid ? (byte)255 : (byte)0xE0;
                var surface = dark ? Tone(0x14, 0.07, surfaceAlpha) : Tone(0xFB, 0.04, surfaceAlpha);
                var pane = dark ? Tone(0x19, 0.09, paneAlpha) : Tone(0xF6, 0.06, paneAlpha);
                var card = dark ? Tone(0x20, 0.11, cardAlpha) : Tone(0xF1, 0.09, cardAlpha);
                theme[WindowBackgroundKey] = new SolidColorBrush(surface);
                theme["ApplicationPageBackgroundThemeBrush"] = new SolidColorBrush(surface);
                theme["LayerFillColorDefaultBrush"] = new SolidColorBrush(pane);
                theme["CardBackgroundFillColorDefaultBrush"] = new SolidColorBrush(card);
                theme["CardStrokeColorDefaultBrush"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            else
            {
                // The solid base colour of Windows 11, where there is no backdrop.
                var page = dark ? Windows.UI.Color.FromArgb(255, 0x20, 0x20, 0x20) : Windows.UI.Color.FromArgb(255, 0xF3, 0xF3, 0xF3);
                theme[WindowBackgroundKey] = new SolidColorBrush(solid ? page : Microsoft.UI.Colors.Transparent);
            }
        }

        // Buttons and drop-downs become pills, cards and dialogs get the larger radius.
        if (material)
        {
            resources["ControlCornerRadius"] = new CornerRadius(16);
            resources["OverlayCornerRadius"] = new CornerRadius(16);
        }
        else
        {
            resources.Remove("ControlCornerRadius");
            resources.Remove("OverlayCornerRadius");
        }
    }

    /// <summary>The accent the Material surfaces are tinted with.</summary>
    private static Windows.UI.Color Accent(AppSettings settings)
    {
        if (settings.Accent == "bluetooth")
        {
            return BluetoothBlue;
        }

        try
        {
            return new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
        }
        catch (Exception)
        {
            return BluetoothBlue;
        }
    }

    /// <summary>The dictionary of one theme among the application's resources, made if missing.</summary>
    private static ResourceDictionary ThemeDictionary(ResourceDictionary resources, string name)
    {
        if (resources.ThemeDictionaries.TryGetValue(name, out var found) && found is ResourceDictionary dictionary)
        {
            return dictionary;
        }

        dictionary = new ResourceDictionary();
        resources.ThemeDictionaries[name] = dictionary;
        return dictionary;
    }

    /// <summary>
    /// Brings a window up to date with the settings: theme, the resources <see cref="Apply"/>
    /// set, and the material behind it.
    /// </summary>
    /// <param name="transient">A panel that comes and goes, as opposed to a window that stays.</param>
    public static void ApplyWindow(Window window, FrameworkElement root, AppSettings settings, bool transient)
    {
        var signature = Signature(settings);
        if (WindowsApplied.TryGetValue(window, out var applied) && applied == signature)
        {
            return;
        }

        WindowsApplied.AddOrUpdate(window, signature);
        window.SystemBackdrop = Backdrop(settings, transient);

        // Theme resources are looked up again when the theme of an element changes. Turning
        // it over once and back makes the controls take the new resources now.
        var theme = Theme(settings);
        root.RequestedTheme = root.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        root.RequestedTheme = theme;
    }

    /// <summary>The material behind a window, or null for a solid background.</summary>
    private static SystemBackdrop? Backdrop(AppSettings settings, bool transient)
    {
        if (IsSolid(settings))
        {
            return null;
        }

        // Mica exists on Windows 11 only; Acrylic stands in for it elsewhere.
        var mica = MicaController.IsSupported();
        return settings.Backdrop switch
        {
            "mica" when mica => new MicaBackdrop { Kind = MicaKind.Base },
            "mica_alt" when mica => new MicaBackdrop { Kind = MicaKind.BaseAlt },
            "auto" when mica && !transient => new MicaBackdrop { Kind = MicaKind.Base },
            _ => new DesktopAcrylicBackdrop(),
        };
    }

    /// <summary>
    /// A style from App.xaml for an element made in code. Its colours follow the theme of
    /// the element, where a brush taken from the resources would keep the application's.
    /// </summary>
    public static Style Style(string key) => (Style)Application.Current.Resources[key];

    /// <summary>Moves a colour towards white (255) or black (0) by the given share.</summary>
    private static Windows.UI.Color Mix(Windows.UI.Color color, byte towards, double share)
    {
        byte Channel(byte value) => (byte)Math.Round(value + (towards - value) * share);
        return Windows.UI.Color.FromArgb(255, Channel(color.R), Channel(color.G), Channel(color.B));
    }

    /// <summary>Mixes <paramref name="share"/> of one colour into another.</summary>
    private static Windows.UI.Color Blend(Windows.UI.Color from, Windows.UI.Color into, double share)
    {
        byte Channel(byte a, byte b) => (byte)Math.Round(a + (b - a) * share);
        return Windows.UI.Color.FromArgb(255, Channel(from.R, into.R), Channel(from.G, into.G), Channel(from.B, into.B));
    }

    /// <summary>
    /// Starts the application anew, open on the settings. The caller exits right after; the
    /// new process waits for it.
    /// </summary>
    public static void Restart(Options options)
    {
        if (Environment.ProcessPath is not { } executable)
        {
            return;
        }

        var arguments = new List<string> { RestartedSwitch, "--open=settings" };
        if (options.Demo)
        {
            arguments.Add("--demo");
        }

        Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = false });
    }
}
