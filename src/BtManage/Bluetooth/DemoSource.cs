using System.Text.Json.Nodes;
using BtManage.Interop;
using Microsoft.UI.Dispatching;

namespace BtManage.Bluetooth;

/// <summary>
/// A made-up set of devices (<c>--demo</c>), fed through the same path as real ones, to look
/// at the interface without the hardware: earbuds with per-bud levels, a mouse, a keyboard
/// without a battery, a controller on charge, a phone, headphones that are switched off, and
/// somebody's headphones nearby. The left earbud drains a step every twenty seconds, so the
/// low state and its notification come up too.
/// </summary>
internal sealed class DemoSource : IDisposable
{
    private const ushort Apple = 0x004C;
    private const int TicksPerStep = 20;

    private const string Earbuds = "demo-earbuds";
    private const string Mouse = "demo-mouse";
    private const string Pad = "demo-pad";

    private readonly DeviceMonitor _monitor;
    private readonly DispatcherQueueTimer _timer;
    private int _tick;

    public DemoSource(DeviceMonitor monitor)
    {
        _monitor = monitor;
        _monitor.Import(History());
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

        _monitor.SetSystemDevices(Devices());
        // AirPods Pro: left draining and worn, right at 70 %, case at 50 % and charging.
        _monitor.Advertisement(0xA1, -48, Hub.ManufacturerData, Apple,
            Message(model: [0x0E, 0x20], status: 0x22, levels: (byte)(0x70 | left), chargeAndCase: 0x45));
        // Somebody's AirPods Max further away, at 60 %.
        _monitor.Advertisement(0xB2, -62, Hub.ManufacturerData, Apple,
            Message(model: [0x0A, 0x20], status: 0x20, levels: 0xF6, chargeAndCase: 0x0F));
        // The controller is on its cable, at 65 %.
        var report = new byte[78];
        report[0] = 0x31;
        report[54] = 0x16;
        _monitor.HidReport(Pad, 0x054C, 0x0CE6, report);
    }

    private static List<SystemDeviceInfo> Devices() =>
    [
        new()
        {
            Id = Earbuds, Name = "AirPods Pro", Address = 0x7C04_D0A1_22F0, Transport = "classic", Connected = true,
            ClassOfDevice = 0x24_0418, VendorSource = 1, VendorId = Apple, ProductId = 0x200E,
            Services = [0x110B, 0x110C, 0x110E, 0x111E],
        },
        new()
        {
            Id = Mouse, Name = "MX Anywhere 3", Address = 0xD1A0_3381_5E02, Transport = "le", Connected = true,
            Appearance = 0x03C2, VendorSource = 2, VendorId = 0x046D, ProductId = 0xB025,
            Services = [0x1812, 0x180F, 0x180A], Battery = 64,
        },
        new()
        {
            Id = "demo-keyboard", Name = "Keychron K5 SE", Address = 0xDC2C_26A0_0611, Transport = "classic",
            Connected = true, ClassOfDevice = 0x2540, Services = [0x1124],
        },
        new()
        {
            Id = Pad, Name = "DualSense Wireless Controller", Address = 0xA0AB_5112_9C4E, Transport = "classic",
            Connected = true, ClassOfDevice = 0x2508, VendorSource = 2, VendorId = 0x054C, ProductId = 0x0CE6,
            Services = [0x1124],
        },
        new()
        {
            Id = "demo-phone", Name = "Pixel 9", Address = 0x3C28_6D40_77B1, Transport = "dual", Connected = true,
            ClassOfDevice = 0x5A_020C, Services = [0x110A, 0x111F, 0x112F, 0x1132],
        },
        new()
        {
            Id = "demo-headphones", Name = "WH-1000XM5", Address = 0x88C9_E8F2_0D35, Transport = "classic",
            Connected = false, ClassOfDevice = 0x24_0404, Services = [0x110B, 0x110E, 0x111E], Battery = 70,
            LastConnectedMs = DateTimeOffset.UtcNow.AddHours(-3).ToUnixTimeMilliseconds(),
        },
    ];

    /// <summary>
    /// A day of history for the earbuds and the mouse, in the format the core exports, so the
    /// chart and the estimate have something to show.
    /// </summary>
    private static string History()
    {
        var now = DateTimeOffset.UtcNow;

        // Minutes ago and the level from then on.
        (int Ago, int Percent, bool Charging)[] left =
            [(1380, 40, false), (1290, 30, false), (1200, 30, true), (1140, 100, true), (1130, 100, false), (150, 100, false), (100, 90, false)];
        (int Ago, int Percent, bool Charging)[] right =
            [(1380, 50, false), (1290, 40, false), (1200, 40, true), (1140, 100, true), (1130, 100, false), (150, 100, false), (120, 90, false), (70, 80, false), (20, 70, false)];
        (int Ago, int Percent, bool Charging)[] mouse =
            [(1400, 71, false), (1100, 70, false), (800, 68, false), (500, 67, false), (200, 65, false), (30, 64, false)];

        // Between two changes the core writes a level down again every quarter of an hour.
        // Do the same here, so the lines come out unbroken.
        JsonArray Series((int Ago, int Percent, bool Charging)[] points)
        {
            var series = new JsonArray();
            for (var i = 0; i < points.Length; i++)
            {
                var until = i + 1 < points.Length ? points[i + 1].Ago : 0;
                for (var ago = points[i].Ago; ago > until; ago -= 15)
                {
                    series.Add(new JsonObject
                    {
                        ["t"] = now.AddMinutes(-ago).ToUnixTimeMilliseconds(),
                        ["percent"] = points[i].Percent,
                        ["charging"] = points[i].Charging,
                    });
                }
            }

            return series;
        }

        return new JsonObject
        {
            ["devices"] = new JsonObject
            {
                [Earbuds] = new JsonObject { ["history"] = new JsonObject { ["left"] = Series(left), ["right"] = Series(right) } },
                [Mouse] = new JsonObject { ["history"] = new JsonObject { ["main"] = Series(mouse) } },
            },
        }.ToJsonString();
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
