using Microsoft.UI.Dispatching;
using Windows.Devices.Radios;

namespace BtManage.Bluetooth;

internal enum BluetoothState
{
    /// <summary>Not determined yet.</summary>
    Unknown,
    On,
    Off,

    /// <summary>The computer has no Bluetooth adapter.</summary>
    Missing,
}

/// <summary>The Bluetooth radio of the computer: its state, and switching it on and off.</summary>
internal sealed class RadioControl
{
    private readonly DispatcherQueue _queue;
    private Radio? _radio;

    public RadioControl(DispatcherQueue queue)
    {
        _queue = queue;
        _ = FindAsync();
    }

    public BluetoothState State { get; private set; } = BluetoothState.Unknown;

    /// <summary>Raised on the UI thread.</summary>
    public event Action? Changed;

    public async Task SetAsync(bool on)
    {
        if (_radio is null)
        {
            return;
        }

        try
        {
            if (await Radio.RequestAccessAsync() == RadioAccessStatus.Allowed)
            {
                await _radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
            }
        }
        catch (Exception)
        {
            // Switching is refused, for example by policy. The state stays as it is.
        }
    }

    private async Task FindAsync()
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            _radio = radios.FirstOrDefault(radio => radio.Kind == RadioKind.Bluetooth);
        }
        catch (Exception)
        {
            _radio = null;
        }

        if (_radio is not null)
        {
            _radio.StateChanged += (_, _) => _queue.TryEnqueue(Update);
        }

        _queue.TryEnqueue(Update);
    }

    private void Update()
    {
        var state = _radio?.State switch
        {
            null => BluetoothState.Missing,
            RadioState.On => BluetoothState.On,
            _ => BluetoothState.Off,
        };
        if (state != State)
        {
            State = state;
            Changed?.Invoke();
        }
    }
}
