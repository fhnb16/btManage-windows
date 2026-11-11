using Microsoft.UI.Dispatching;

namespace BtManage.Bluetooth;

/// <summary>
/// Feeds synthetic advertisements through the same path as real ones (<c>--demo</c>), to look
/// at the interface without earbuds at hand. The left earbud drains one step every few
/// seconds, so the low state and its notification come up too.
/// </summary>
internal sealed class DemoSource : IDisposable
{
    private const ushort Apple = 0x004C;
    private const int TicksPerStep = 6;

    private readonly DeviceMonitor _monitor;
    private readonly DispatcherQueueTimer _timer;
    private int _tick;

    public DemoSource(DeviceMonitor monitor)
    {
        _monitor = monitor;
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => Send();
        _timer.Start();
        Send();
    }

    public void Dispose() => _timer.Stop();

    private void Send()
    {
        var left = 9 - _tick / TicksPerStep % 9;
        _tick++;

        // AirPods Pro: left draining and worn, right at 70 %, case at 50 % and charging.
        _monitor.Ingest(0xA1, -48, Apple, Message(model: [0x0E, 0x20], status: 0x22, levels: (byte)(0x70 | left), chargeAndCase: 0x45));
        // AirPods Max further away, at 60 %.
        _monitor.Ingest(0xB2, -62, Apple, Message(model: [0x0A, 0x20], status: 0x20, levels: 0xF6, chargeAndCase: 0x0F));
    }

    /// <summary>A proximity pairing message; see apple/proximity.rs in the core for the layout.</summary>
    private static byte[] Message(byte[] model, byte status, byte levels, byte chargeAndCase)
    {
        var data = new byte[27];
        data[0] = 0x07;
        data[1] = 0x19;
        data[2] = 0x01;
        data[3] = model[0];
        data[4] = model[1];
        data[5] = status;
        data[6] = levels;
        data[7] = chargeAndCase;
        return data;
    }
}
