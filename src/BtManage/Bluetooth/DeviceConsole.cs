using System.Globalization;
using System.Text;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace BtManage.Bluetooth;

/// <summary>
/// Raw traffic with a device, for the developer console: GATT for Bluetooth LE devices
/// (services, read, write, subscribe) and RFCOMM serial channels of Bluetooth Classic (SPP
/// and makers' own). Every line it has to say comes through <see cref="Line"/>, on any thread.
/// </summary>
internal sealed class DeviceConsole : IDisposable
{
    private BluetoothLEDevice? _le;
    private readonly List<GattCharacteristic> _characteristics = [];
    private StreamSocket? _socket;
    private DataWriter? _writer;

    /// <summary>A line of the exchange: what was sent, what came back, what went wrong.</summary>
    public event Action<string>? Line;

    /// <summary>The characteristics of the GATT server connected to, after <see cref="ConnectGattAsync"/>.</summary>
    public IReadOnlyList<GattCharacteristic> Characteristics => _characteristics;

    /// <summary>What the console is connected to, in words; empty when nothing.</summary>
    public string Connection { get; private set; } = string.Empty;

    private void Note(string text) => Line?.Invoke(text);

    /// <summary>Connects to a Bluetooth LE device and lists its services and characteristics.</summary>
    public async Task ConnectGattAsync(ulong address)
    {
        Close();
        try
        {
            _le = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if (_le is null)
            {
                Note("GATT: Windows does not know a Bluetooth LE device at this address");
                return;
            }

            var services = await _le.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            Note($"GATT {_le.Name}: {services.Status}, {services.Services.Count} services");
            foreach (var service in services.Services)
            {
                Note($"  service {Name(service.Uuid)}");
                var found = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                foreach (var characteristic in found.Characteristics)
                {
                    _characteristics.Add(characteristic);
                    Note($"    {Name(characteristic.Uuid)} [{characteristic.CharacteristicProperties}]");
                }
            }

            Connection = $"GATT {_le.Name}";
        }
        catch (Exception exception)
        {
            Note($"GATT: {exception.Message}");
        }
    }

    public async Task ReadAsync(Guid uuid)
    {
        if (Find(uuid) is not { } characteristic)
        {
            return;
        }

        try
        {
            var result = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
            Note($"GATT read {Name(uuid)}, {result.Status}: {Describe(Bytes(result.Value))}");
        }
        catch (Exception exception)
        {
            Note($"GATT read {Name(uuid)}: {exception.Message}");
        }
    }

    public async Task WriteAsync(Guid uuid, byte[] data)
    {
        if (Find(uuid) is not { } characteristic)
        {
            return;
        }

        var withResponse = characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write);
        var option = withResponse ? GattWriteOption.WriteWithResponse : GattWriteOption.WriteWithoutResponse;
        try
        {
            var result = await characteristic.WriteValueWithResultAsync(CryptographicBuffer.CreateFromByteArray(data), option);
            Note($"GATT write {Name(uuid)} -> {Hex(data)}: {result.Status}");
        }
        catch (Exception exception)
        {
            Note($"GATT write {Name(uuid)}: {exception.Message}");
        }
    }

    /// <summary>Asks the device to send each change of a characteristic, as a notification or an indication.</summary>
    public async Task SubscribeAsync(Guid uuid)
    {
        if (Find(uuid) is not { } characteristic)
        {
            return;
        }

        var indicate = characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate);
        var value = indicate
            ? GattClientCharacteristicConfigurationDescriptorValue.Indicate
            : GattClientCharacteristicConfigurationDescriptorValue.Notify;
        characteristic.ValueChanged -= OnValueChanged;
        characteristic.ValueChanged += OnValueChanged;
        try
        {
            var status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(value);
            Note($"GATT subscribe {Name(uuid)}: {status}");
        }
        catch (Exception exception)
        {
            Note($"GATT subscribe {Name(uuid)}: {exception.Message}");
        }
    }

    private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args) =>
        Note($"GATT notified {Name(sender.Uuid)}: {Describe(Bytes(args.CharacteristicValue))}");

    /// <summary>Opens a serial channel to the service <paramref name="service"/> of a paired device.</summary>
    public async Task ConnectRfcommAsync(ulong address, Guid service)
    {
        Close();
        try
        {
            var device = await BluetoothDevice.FromBluetoothAddressAsync(address);
            if (device is null)
            {
                Note("RFCOMM: Windows does not know a Bluetooth Classic device at this address");
                return;
            }

            var services = await device.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(service), BluetoothCacheMode.Uncached);
            if (services.Services.Count == 0)
            {
                Note($"RFCOMM: {device.Name} does not offer {service} ({services.Error})");
                return;
            }

            var channel = services.Services[0];
            var socket = new StreamSocket();
            await socket.ConnectAsync(channel.ConnectionHostName, channel.ConnectionServiceName);
            _socket = socket;
            _writer = new DataWriter(socket.OutputStream);
            Connection = $"RFCOMM {device.Name}";
            Note($"RFCOMM: connected to {device.Name}, {service}");
            _ = ReadLoopAsync(socket);
        }
        catch (Exception exception)
        {
            Note($"RFCOMM: {exception.Message}");
        }
    }

    private async Task ReadLoopAsync(StreamSocket socket)
    {
        using var reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
        try
        {
            while (true)
            {
                var count = await reader.LoadAsync(1024);
                if (count == 0)
                {
                    break;
                }

                var bytes = new byte[count];
                reader.ReadBytes(bytes);
                Note($"RFCOMM <- {Describe(bytes)}");
            }
        }
        catch (Exception exception)
        {
            if (_socket == socket)
            {
                Note($"RFCOMM: {exception.Message}");
            }
        }

        if (_socket == socket)
        {
            Note("RFCOMM: closed");
            Connection = string.Empty;
        }
    }

    public async Task SendRfcommAsync(byte[] data)
    {
        if (_writer is not { } writer)
        {
            Note("RFCOMM: not connected");
            return;
        }

        try
        {
            writer.WriteBytes(data);
            await writer.StoreAsync();
            Note($"RFCOMM -> {Hex(data)}");
        }
        catch (Exception exception)
        {
            Note($"RFCOMM: {exception.Message}");
        }
    }

    /// <summary>Closes whatever the console has open.</summary>
    public void Close()
    {
        foreach (var characteristic in _characteristics)
        {
            characteristic.ValueChanged -= OnValueChanged;
        }

        _characteristics.Clear();
        _le?.Dispose();
        _le = null;
        _writer?.Dispose();
        _writer = null;
        var socket = _socket;
        _socket = null;
        socket?.Dispose();
        Connection = string.Empty;
    }

    public void Dispose() => Close();

    private GattCharacteristic? Find(Guid uuid)
    {
        var found = _characteristics.FirstOrDefault(characteristic => characteristic.Uuid == uuid);
        if (found is null)
        {
            Note(_le is null ? "GATT: not connected" : $"GATT: no characteristic {Name(uuid)}");
        }

        return found;
    }

    private static byte[] Bytes(IBuffer? buffer)
    {
        if (buffer is null)
        {
            return [];
        }

        CryptographicBuffer.CopyToByteArray(buffer, out var bytes);
        return bytes ?? [];
    }

    /// <summary>"04 00 1A".</summary>
    public static string Hex(byte[] bytes) => string.Join(' ', bytes.Take(64).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)))
        + (bytes.Length > 64 ? $" … ({bytes.Length} bytes)" : string.Empty);

    /// <summary>Bytes in hexadecimal and, where they read as text, as text.</summary>
    private static string Describe(byte[] bytes)
    {
        var text = new string(Encoding.UTF8.GetString(bytes).Where(c => !char.IsControl(c)).ToArray());
        return text.Length > 0 ? $"{Hex(bytes)}  \"{text}\"" : Hex(bytes);
    }

    /// <summary>"2A19" for a UUID on the Bluetooth base, the whole UUID for any other.</summary>
    public static string Name(Guid uuid)
    {
        var text = uuid.ToString();
        return text.StartsWith("0000", StringComparison.Ordinal) && text.EndsWith("-0000-1000-8000-00805f9b34fb", StringComparison.Ordinal)
            ? text.Substring(4, 4).ToUpperInvariant()
            : text;
    }

    /// <summary>A UUID from "2A19" or a whole UUID; null for anything else.</summary>
    public static Guid? Uuid(string text)
    {
        var clean = text.Trim();
        if (clean.Length == 4 && ushort.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number))
        {
            return BluetoothUuidHelper.FromShortId(number);
        }

        return Guid.TryParse(clean, out var uuid) ? uuid : null;
    }

    /// <summary>Bytes from hexadecimal ("04 00 0f", "04000F"); null when it is not.</summary>
    public static byte[]? ParseHex(string text)
    {
        var digits = new string(text.Where(c => !char.IsWhiteSpace(c) && c is not ':' and not ',').ToArray());
        if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            digits = digits[2..];
        }

        if (digits.Length == 0 || digits.Length % 2 != 0)
        {
            return null;
        }

        try
        {
            return Convert.FromHexString(digits);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>The 48-bit address in "AA:BB:CC:DD:EE:FF".</summary>
    public static ulong? Address(string? text) =>
        text is not null && ulong.TryParse(text.Replace(":", string.Empty, StringComparison.Ordinal), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
