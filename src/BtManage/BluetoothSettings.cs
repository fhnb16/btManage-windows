namespace BtManage;

internal static class BluetoothSettings
{
    /// <summary>Opens the Bluetooth page of Windows Settings.</summary>
    public static void Open() =>
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:bluetooth"));
}
