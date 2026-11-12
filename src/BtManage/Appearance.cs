using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace BtManage;

/// <summary>Theme, language and window material, as chosen in the settings.</summary>
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

    /// <summary>The theme to start the application with, or null to follow the system.</summary>
    public static ApplicationTheme? Theme(AppSettings settings) => settings.Theme switch
    {
        "light" => ApplicationTheme.Light,
        "dark" => ApplicationTheme.Dark,
        _ => null,
    };

    /// <summary>The blue of the Bluetooth mark.</summary>
    private static readonly Windows.UI.Color BluetoothBlue = Windows.UI.Color.FromArgb(255, 0x00, 0x82, 0xFC);

    /// <summary>
    /// Replaces the accent colour of Windows with Bluetooth blue, if the user chose that.
    /// The controls take their accent from these resources; they must be set before any
    /// window is created.
    /// </summary>
    public static void ApplyAccent(Application application, AppSettings settings)
    {
        if (settings.Accent != "bluetooth")
        {
            return;
        }

        var resources = application.Resources;
        resources["SystemAccentColor"] = BluetoothBlue;
        // The lighter shades are used on dark surfaces, the darker ones on light surfaces.
        resources["SystemAccentColorLight1"] = Mix(BluetoothBlue, 255, 0.18);
        resources["SystemAccentColorLight2"] = Mix(BluetoothBlue, 255, 0.36);
        resources["SystemAccentColorLight3"] = Mix(BluetoothBlue, 255, 0.54);
        resources["SystemAccentColorDark1"] = Mix(BluetoothBlue, 0, 0.16);
        resources["SystemAccentColorDark2"] = Mix(BluetoothBlue, 0, 0.32);
        resources["SystemAccentColorDark3"] = Mix(BluetoothBlue, 0, 0.48);
    }

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

    /// <summary>The brush behind the windows in the Material design.</summary>
    private const string MaterialSurfaceKey = "BtmMaterialSurfaceBrush";

    public static bool IsMaterial(AppSettings settings) => settings.Design == "material";

    /// <summary>
    /// Turns the Fluent look into Material Design, if the user chose that: surfaces in
    /// solid tones tinted with the accent instead of Mica and Acrylic, cards without a
    /// border, rounder corners. The controls stay the ones of Windows.
    ///
    /// Like the accent, this replaces resources the controls read when they are created, so
    /// it must run before the first window exists and after <see cref="ApplyAccent"/>.
    /// </summary>
    public static void ApplyDesign(Application application, AppSettings settings)
    {
        if (!IsMaterial(settings))
        {
            return;
        }

        var accent = BluetoothBlue;
        if (settings.Accent != "bluetooth")
        {
            try
            {
                accent = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            }
            catch (Exception)
            {
                // Keep the blue.
            }
        }

        // Material surfaces are neutral tones with a little of the accent in them: the
        // window lightest (or darkest), the content pane a step off it, cards another step.
        var dark = application.RequestedTheme == ApplicationTheme.Dark;
        Windows.UI.Color Tone(byte grey, double share) =>
            Blend(Windows.UI.Color.FromArgb(255, grey, grey, grey), accent, share);
        var surface = dark ? Tone(0x14, 0.07) : Tone(0xFB, 0.04);
        var pane = dark ? Tone(0x19, 0.09) : Tone(0xF6, 0.06);
        var card = dark ? Tone(0x20, 0.11) : Tone(0xF1, 0.09);

        var resources = application.Resources;
        resources[MaterialSurfaceKey] = new SolidColorBrush(surface);
        resources["ApplicationPageBackgroundThemeBrush"] = new SolidColorBrush(surface);
        resources["LayerFillColorDefaultBrush"] = new SolidColorBrush(pane);
        resources["CardBackgroundFillColorDefaultBrush"] = new SolidColorBrush(card);
        resources["CardStrokeColorDefaultBrush"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        // Buttons and drop-downs become pills, cards and dialogs get the larger radius.
        resources["ControlCornerRadius"] = new CornerRadius(16);
        resources["OverlayCornerRadius"] = new CornerRadius(16);
    }

    /// <summary>Puts the chosen material behind a window.</summary>
    /// <param name="transient">A panel that comes and goes, as opposed to a window that stays.</param>
    public static void ApplyBackdrop(Window window, FrameworkElement root, AppSettings settings, bool transient)
    {
        if (IsMaterial(settings))
        {
            // Material Design has opaque surfaces: no Mica, no Acrylic.
            window.SystemBackdrop = null;
            if (root is Microsoft.UI.Xaml.Controls.Panel surface)
            {
                surface.Background = (Brush)Application.Current.Resources[MaterialSurfaceKey];
            }

            return;
        }

        // Mica exists on Windows 11 only; Acrylic stands in for it elsewhere.
        var mica = MicaController.IsSupported();
        SystemBackdrop? backdrop = settings.Backdrop switch
        {
            "mica" when mica => new MicaBackdrop { Kind = MicaKind.Base },
            "mica_alt" when mica => new MicaBackdrop { Kind = MicaKind.BaseAlt },
            "auto" when mica && !transient => new MicaBackdrop { Kind = MicaKind.Base },
            _ when DesktopAcrylicController.IsSupported() => new DesktopAcrylicBackdrop(),
            _ => null,
        };

        window.SystemBackdrop = backdrop;
        if (root is Microsoft.UI.Xaml.Controls.Panel panel)
        {
            panel.Background = backdrop is null
                ? (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"]
                : null;
        }
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
