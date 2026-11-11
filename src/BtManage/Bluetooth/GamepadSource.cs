using System.Runtime.InteropServices.WindowsRuntime;
using BtManage.Interop;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Gaming.Input;
using Windows.Storage;
using Windows.System.Power;

namespace BtManage.Bluetooth;

/// <summary>
/// Battery of game controllers that do not report it the way other devices do.
///
/// Xbox controllers: through the system's game controller interface. PlayStation
/// controllers: from their HID input report, which the core parses. Over Bluetooth a
/// PlayStation controller sends the report with the battery only after it has been asked for
/// a feature report; this is done only when the user has switched it on, since it changes
/// what the controller sends to every program.
///
/// Not tested with real controllers yet.
/// </summary>
internal sealed class GamepadSource : IDisposable
{
    private const int Sony = 0x054C;
    private static readonly int[] DualShock4 = [0x05C4, 0x09CC];
    private static readonly int[] DualSense = [0x0CE6, 0x0DF2];

    /// <summary>Controllers are polled this often, in monitor ticks.</summary>
    private const int TicksPerPoll = 12;

    private readonly DeviceMonitor _monitor;
    private readonly AppSettings _settings;
    private int _ticks = TicksPerPoll - 2;
    private int _polling;

    public GamepadSource(DeviceMonitor monitor, AppSettings settings)
    {
        _monitor = monitor;
        _settings = settings;
        // Touching the list makes the system start filling it.
        _ = RawGameController.RawGameControllers.Count;
        _monitor.Tick += OnTick;
    }

    public void Dispose() => _monitor.Tick -= OnTick;

    private void OnTick()
    {
        if (++_ticks % TicksPerPoll == 0 && Interlocked.Exchange(ref _polling, 1) == 0)
        {
            _ = PollAsync();
        }
    }

    private async Task PollAsync()
    {
        try
        {
            var pads = _monitor.Current.Devices
                .Where(device => device is { Connected: true, VendorId: not null, ProductId: not null })
                .ToList();

            foreach (var controller in RawGameController.RawGameControllers)
            {
                var pad = pads.FirstOrDefault(device =>
                    device.VendorId == controller.HardwareVendorId && device.ProductId == controller.HardwareProductId);
                if (pad is not null && controller.TryGetBatteryReport() is
                    {
                        RemainingCapacityInMilliwattHours: { } remaining,
                        FullChargeCapacityInMilliwattHours: > 0 and var full,
                    } report)
                {
                    _monitor.Battery(pad.Id, Source.Gamepad, remaining * 100 / full, report.Status == BatteryStatus.Charging);
                }
            }

            foreach (var pad in pads.Where(device => device.VendorId == Sony))
            {
                await ReadPlayStationAsync(pad);
            }
        }
        catch (Exception)
        {
            // A controller that went away mid-read; try again at the next poll.
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private async Task ReadPlayStationAsync(Device pad)
    {
        var product = pad.ProductId!.Value;
        if (!DualShock4.Contains(product) && !DualSense.Contains(product))
        {
            return;
        }

        // The gamepad collection (usage page 1, usage 5) of this very device.
        var selector = HidDevice.GetDeviceSelector(0x01, 0x05, Sony, (ushort)product);
        var interfaces = await DeviceInformation.FindAllAsync(selector, ["System.Devices.ContainerId"]);
        var match = interfaces.FirstOrDefault(candidate =>
            candidate.Properties.TryGetValue("System.Devices.ContainerId", out var container)
            && container is Guid guid
            && guid.ToString("D") == pad.Id);
        if (match is null)
        {
            return;
        }

        var access = _settings.PlayStationFullReports ? FileAccessMode.ReadWrite : FileAccessMode.Read;
        using var hid = await HidDevice.FromIdAsync(match.Id, access);
        if (hid is null)
        {
            return;
        }

        if (_settings.PlayStationFullReports)
        {
            // Reading this report is what switches the controller to full input reports.
            await hid.GetFeatureReportAsync(DualSense.Contains(product) ? (ushort)0x05 : (ushort)0x02);
        }

        // One report is enough; controllers send hundreds a second.
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnReport(HidDevice sender, HidInputReportReceivedEventArgs args) =>
            received.TrySetResult(args.Report.Data.ToArray());

        hid.InputReportReceived += OnReport;
        try
        {
            if (await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(2))) == received.Task)
            {
                _monitor.HidReport(pad.Id, Sony, (ushort)product, await received.Task);
            }
        }
        finally
        {
            hid.InputReportReceived -= OnReport;
        }
    }
}
