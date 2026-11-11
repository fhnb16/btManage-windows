using System.Text.Json.Serialization;

namespace BtManage.Interop;

internal enum Kind
{
    Other,
    Earbuds,
    Headphones,
    Headset,
    Speaker,
    Mouse,
    Keyboard,
    Gamepad,
    Remote,
    Phone,
    Tablet,
    Computer,
    Watch,
    Wearable,
    Car,
}

internal enum Role
{
    Left,
    Right,
    Case,
    Main,
}

/// <summary>Where a battery level came from.</summary>
internal enum Source
{
    Advert,
    System,
    Gatt,
    Hid,
    Gamepad,
}

internal enum Transport
{
    Classic,
    Le,
    Dual,
}

/// <summary>How the core came to know a device.</summary>
internal enum Origin
{
    System,
    Apple,
    [JsonStringEnumMemberName("fast_pair")]
    FastPair,
}

/// <summary>
/// The device list, as the core reports it: connected devices first, then nearby ones, then
/// the rest.
/// </summary>
internal sealed record Snapshot(IReadOnlyList<Device> Devices)
{
    public static Snapshot Empty { get; } = new([]);
}

internal sealed record Device(
    string Id,
    Origin Origin,
    string Name,
    string? Model,
    Kind Kind,
    string? Address,
    Transport? Transport,
    bool Paired,
    bool Connected,
    bool Nearby,
    IReadOnlyList<Cell> Cells,
    int? Rssi,
    int? EstimateMin,
    int? VendorId,
    int? ProductId,
    IReadOnlyList<string> Profiles,
    long? LastConnectedMs)
{
    public Cell? Cell(Role role) => Cells.FirstOrDefault(cell => cell.Role == role);

    /// <summary>Two earbuds and a case, as opposed to one battery for the whole device.</summary>
    public bool HasEarbudCells => Cells.Any(cell => cell.Role is Role.Left or Role.Right);

    /// <summary>Connected, or heard over the air within the last minute.</summary>
    public bool IsActive => Connected || Nearby;

    /// <summary>The level that runs out first, the case aside.</summary>
    public Cell? LowestCell => Cells.Where(cell => cell.Role != Role.Case).MinBy(cell => cell.Percent);
}

/// <summary>One battery: an earbud, a case, or the single battery of a device.</summary>
internal sealed record Cell(Role Role, int Percent, bool Charging, bool? InEar, long AgeMs, Source Source)
{
    /// <summary>A level older than this is shown with its age.</summary>
    public const long FreshMs = 2 * 60_000;

    public bool IsFresh => AgeMs < FreshMs;

    public bool IsLow(int threshold) => Percent <= threshold && !Charging;
}

/// <summary>The recorded levels of one battery, oldest first.</summary>
internal sealed record RoleHistory(Role Role, IReadOnlyList<HistoryPoint> Points);

/// <param name="T">Unix time in milliseconds.</param>
internal sealed record HistoryPoint(long T, int Percent, bool Charging);

/// <summary>A device paired with Windows, as this application describes it to the core.</summary>
internal sealed record SystemDeviceInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public ulong? Address { get; init; }

    /// <summary>"classic", "le" or "dual".</summary>
    public string? Transport { get; init; }
    public bool Connected { get; init; }
    public uint? ClassOfDevice { get; init; }
    public ushort? Appearance { get; init; }

    /// <summary>Who assigned <see cref="VendorId"/>: 1 the Bluetooth SIG, 2 the USB-IF.</summary>
    public byte? VendorSource { get; init; }
    public ushort? VendorId { get; init; }
    public ushort? ProductId { get; init; }
    public IReadOnlyList<ushort> Services { get; init; } = [];
    public byte? Battery { get; init; }
    public long? LastConnectedMs { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Snapshot))]
[JsonSerializable(typeof(IReadOnlyList<RoleHistory>))]
[JsonSerializable(typeof(IReadOnlyList<SystemDeviceInfo>))]
internal sealed partial class CoreJson : JsonSerializerContext;
