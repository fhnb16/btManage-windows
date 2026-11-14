using Microsoft.UI.Dispatching;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace BtManage.Bluetooth;

/// <summary>
/// The search for devices to pair with, and the pairing: Bluetooth Classic devices that
/// are discoverable and Bluetooth LE devices that advertise, as Windows Settings finds them.
/// Events come on the UI thread.
/// </summary>
internal sealed class PairingSearch : IDisposable
{
    private readonly DispatcherQueue _queue;
    private readonly List<DeviceWatcher> _watchers = [];
    private readonly Dictionary<string, DeviceInformation> _found = [];

    public PairingSearch(DispatcherQueue queue)
    {
        _queue = queue;
        string[] selectors =
        [
            BluetoothDevice.GetDeviceSelectorFromPairingState(false),
            BluetoothLEDevice.GetDeviceSelectorFromPairingState(false),
        ];
        string[] properties = ["System.Devices.Aep.IsPaired"];
        foreach (var selector in selectors)
        {
            var watcher = DeviceInformation.CreateWatcher(selector, properties, DeviceInformationKind.AssociationEndpoint);
            watcher.Added += (_, info) => _queue.TryEnqueue(() => Add(info));
            watcher.Updated += (_, update) => _queue.TryEnqueue(() => Update(update));
            watcher.Removed += (_, update) => _queue.TryEnqueue(() => Remove(update.Id));
            _watchers.Add(watcher);
        }
    }

    /// <summary>Devices found that have a name, by name.</summary>
    public IReadOnlyList<DeviceInformation> Found =>
        [.. _found.Values.Where(info => !string.IsNullOrWhiteSpace(info.Name)).OrderBy(info => info.Name)];

    /// <summary>Raised on the UI thread when the list changed.</summary>
    public event Action? Changed;

    public void Start()
    {
        foreach (var watcher in _watchers)
        {
            if (watcher.Status is DeviceWatcherStatus.Created or DeviceWatcherStatus.Stopped or DeviceWatcherStatus.Aborted)
            {
                watcher.Start();
            }
        }
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            {
                watcher.Stop();
            }
        }
    }

    private void Add(DeviceInformation info)
    {
        _found[info.Id] = info;
        Changed?.Invoke();
    }

    private void Update(DeviceInformationUpdate update)
    {
        if (_found.TryGetValue(update.Id, out var info))
        {
            info.Update(update);
            Changed?.Invoke();
        }
    }

    private void Remove(string id)
    {
        if (_found.Remove(id))
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Pairs with a device. Where the device shows or asks for a code, <paramref name="confirm"/>
    /// is asked whether the code matches; a device that wants a code typed in gets "0000",
    /// which older headsets use.
    /// </summary>
    public static async Task<DevicePairingResultStatus> PairAsync(DeviceInformation info, Func<string, Task<bool>> confirm)
    {
        var custom = info.Pairing.Custom;
        async void OnRequested(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs args)
        {
            var deferral = args.GetDeferral();
            try
            {
                switch (args.PairingKind)
                {
                    case DevicePairingKinds.ConfirmOnly:
                        args.Accept();
                        break;
                    case DevicePairingKinds.DisplayPin:
                    case DevicePairingKinds.ConfirmPinMatch:
                        if (await confirm(args.Pin))
                        {
                            args.Accept();
                        }

                        break;
                    case DevicePairingKinds.ProvidePin:
                        args.Accept("0000");
                        break;
                }
            }
            finally
            {
                deferral.Complete();
            }
        }

        custom.PairingRequested += OnRequested;
        try
        {
            var kinds = DevicePairingKinds.ConfirmOnly | DevicePairingKinds.DisplayPin
                | DevicePairingKinds.ConfirmPinMatch | DevicePairingKinds.ProvidePin;
            var result = await custom.PairAsync(kinds);
            return result.Status;
        }
        finally
        {
            custom.PairingRequested -= OnRequested;
        }
    }
}
