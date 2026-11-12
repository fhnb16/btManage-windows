# To do: Windows

## Needs trying on real hardware

- Connect and disconnect of audio devices (`Bluetooth/AudioConnection.cs`). Finding the audio filters is checked; sending the request is not.
- Removing a pairing.
- Fast Pair earbuds, Xbox controllers on Bluetooth Classic, DUALSHOCK 4 and DualSense.
- Windows 10: Acrylic fallback, tray glyph on the older icon font, the installer.
- Windows on ARM.
- The installer (`installer/btManage.iss`) has not been built yet, neither locally nor on a runner.
- A click on a notification opening the window on the device.
- The Material design (`Appearance.ApplyDesign`): written without a build or a look at the result. Check both windows in light and dark, with the system accent and with Bluetooth blue.

## Devices and data

- Audio codec in use. Windows gives applications no way to ask; the only known route is tracing the Bluetooth audio driver, which needs administrator rights.
- AirPods controls: noise control, conversation awareness, ear detection. These go over AAP on L2CAP, which Windows applications cannot open without a kernel driver. The packets themselves are in the core (`apple::aap`), as Android uses them.
- Noise control of other makers' earbuds over RFCOMM, which Windows applications can open (`RfcommDeviceService`): Samsung Galaxy Buds, Sony, Soundcore, Nothing. Each protocol goes into the core, so Android uses the same code; each needs a pair of those earbuds to be tried on. Earbuds of the cheaper makers (HOCO and the like) have no public description of their protocol, and many have no way to switch noise control from a phone or computer at all.
- Battery of Bluetooth Classic keyboards and mice that report it only in a HID report (generic battery strength usage).
- Direct read of the GATT Battery Service for Bluetooth LE devices Windows does not keep a level for.
- Phones: the level a phone reports to a hands-free unit.
- Tell two pairs of the same earbud model apart.
- Per-device options: a name of your own, an icon, hide from the list.

## Interface

- Tray menu in the dark theme. The Win32 menu follows the light theme only.
- Battery level in the tray icon itself, for a device of the user's choice.
- Settings without restart for theme, accent and language.
- Pair a new device from the window instead of sending the user to Windows Settings.
- Set a device as the default audio output.
- Second language pass by a native English speaker.

## Distribution

- Code signing. The workflow is ready for a certificate (`WINDOWS_CERTIFICATE_BASE64`, `WINDOWS_CERTIFICATE_PASSWORD`); the uninstaller inside the installer is not signed by it yet.
- Trim the self-contained build, about 170 MB unpacked.
- MSIX package and the Microsoft Store.
- winget manifest.

## Beyond Bluetooth devices

The application is meant to grow into a general companion utility:

- AirPlay: receive audio and screen from Apple devices, and send to AirPlay receivers.
- AirDrop: receive files from, and send files to, Apple devices nearby.
- Nearby Share / Quick Share with Android devices.
- Clipboard and notification sync with a paired phone.
