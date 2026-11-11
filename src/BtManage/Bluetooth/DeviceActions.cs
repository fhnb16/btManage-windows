using System.Globalization;
using BtManage.Interop;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace BtManage.Bluetooth;

/// <summary>What the user can do to a device.</summary>
internal static class DeviceActions
{
    /// <summary>
    /// Whether the device can be connected and disconnected from here. Windows offers that
    /// only for audio devices; everything else connects on its own.
    /// </summary>
    public static Task<bool> CanSwitchAudioAsync(Device device) =>
        device.Paired
            ? Task.Run(() => AudioConnection.Supports(SystemDeviceSource.NodesOf(device.Id)))
            : Task.FromResult(false);

    public static Task<bool> ConnectAudioAsync(Device device) =>
        Task.Run(() => AudioConnection.Connect(SystemDeviceSource.NodesOf(device.Id)));

    public static Task<bool> DisconnectAudioAsync(Device device) =>
        Task.Run(() => AudioConnection.Disconnect(SystemDeviceSource.NodesOf(device.Id)));

    /// <summary>Removes the pairing, as "Remove device" in Windows Settings does.</summary>
    public static async Task<bool> UnpairAsync(Device device)
    {
        if (device.Address is not { } text
            || !ulong.TryParse(text.Replace(":", string.Empty), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address))
        {
            return false;
        }

        try
        {
            DeviceInformation? information = null;
            if (device.Transport is Transport.Le)
            {
                using var le = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
                information = le?.DeviceInformation;
            }
            else
            {
                using var classic = await BluetoothDevice.FromBluetoothAddressAsync(address);
                information = classic?.DeviceInformation;
            }

            if (information is null)
            {
                return false;
            }

            var result = await information.Pairing.UnpairAsync();
            return result.Status is DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
