using System.Text.Json.Serialization;

namespace BtManage.Interop;

/// <summary>State of the nearby devices, as the core reports it. Nearest device first.</summary>
internal sealed record Snapshot(IReadOnlyList<Device> Devices)
{
    public static Snapshot Empty { get; } = new([]);
}

internal sealed record Device(
    string Id,
    string Vendor,
    int ModelId,
    string? Model,
    string Form,
    Cell? Left,
    Cell? Right,
    Cell? Case,
    Cell? Main,
    int Rssi,
    long AgeMs)
{
    /// <summary>One battery for the whole device, as opposed to two earbuds and a case.</summary>
    public bool IsHeadphones => Form == "headphones";
}

/// <summary>One battery: an earbud, a case, or a whole pair of headphones.</summary>
internal sealed record Cell(int Percent, bool Charging, bool? InEar)
{
    /// <summary>At or below this level, and not charging, a battery counts as low.</summary>
    public const int LowThreshold = 20;

    public bool IsLow => Percent <= LowThreshold && !Charging;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(Snapshot))]
internal sealed partial class SnapshotJson : JsonSerializerContext;
