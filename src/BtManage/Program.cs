using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace BtManage;

/// <summary>Command line switches.</summary>
/// <param name="Demo">Show made-up devices instead of the real ones.</param>
/// <param name="Tray">Start in the tray without opening the window, as at Windows startup.</param>
/// <param name="Flyout">Open the tray panel right after start.</param>
/// <param name="Open">Open the window on this device, or on "settings" (<c>--open=id</c>).</param>
/// <param name="Restarted">Started by the application itself; wait for the old process to go.</param>
/// <param name="UpdateFrom">Install the application from this archive (<c>--update-from=file.zip</c>).</param>
internal sealed record Options(bool Demo, bool Tray, bool Flyout, bool Restarted, string? Open, string? UpdateFrom)
{
    private const string OpenSwitch = "--open=";
    private const string UpdateSwitch = "--update-from=";

    public static Options Parse(string[] args) => new(
        Demo: Has(args, "--demo"),
        Tray: Has(args, Autostart.TraySwitch),
        Flyout: Has(args, "--flyout"),
        Restarted: Has(args, Appearance.RestartedSwitch),
        Open: Value(args, OpenSwitch),
        UpdateFrom: Value(args, UpdateSwitch));

    private static string? Value(string[] args, string name) =>
        args.FirstOrDefault(arg => arg.StartsWith(name, StringComparison.OrdinalIgnoreCase))?[name.Length..];

    private static bool Has(string[] args, string name) => args.Contains(name, StringComparer.OrdinalIgnoreCase);
}

internal static class Program
{
    private const string ShowEvent = @"Local\BtManage.Show";

    /// <summary>Another copy of the application was started: bring the window up.</summary>
    public static event Action? ShowRequested;

    [STAThread]
    private static void Main(string[] args)
    {
        var options = Options.Parse(args);

        // One instance per session. A second start asks the first to show its window.
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEvent, out var first);
        for (var attempt = 0; !first && options.Restarted && attempt < 50; attempt++)
        {
            // The process that started this one is on its way out.
            show.Dispose();
            Thread.Sleep(100);
            show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEvent, out first);
        }

        using var handle = show;
        if (!first)
        {
            if (!options.Tray)
            {
                show.Set();
            }

            return;
        }

        new Thread(() =>
        {
            while (show.WaitOne())
            {
                ShowRequested?.Invoke();
            }
        })
        { IsBackground = true, Name = "Show requests" }.Start();

        // Demo mode keeps its own settings and leaves the real ones alone.
        var settings = AppSettings.Load(options.Demo ? AppPaths.DemoSettings : AppPaths.Settings);

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            Appearance.ApplyLanguage(settings);
            // The application registers itself as Application.Current.
            new App(options, settings);
        });
    }
}
