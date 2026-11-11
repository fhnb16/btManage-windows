using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace BtManage;

/// <summary>Command line switches.</summary>
/// <param name="Demo">Feed synthetic advertisements instead of scanning.</param>
/// <param name="Show">Open the flyout right after start.</param>
internal sealed record Options(bool Demo, bool Show)
{
    public static Options Parse(string[] args) => new(
        Demo: args.Contains("--demo", StringComparer.OrdinalIgnoreCase),
        Show: args.Contains("--show", StringComparer.OrdinalIgnoreCase));
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // One tray icon per session is enough.
        using var instance = new Mutex(initiallyOwned: true, @"Local\BtManage.Instance", out var first);
        if (!first)
        {
            return;
        }

        var options = Options.Parse(args);
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            // The application registers itself as Application.Current.
            new App(options);
        });
    }
}
