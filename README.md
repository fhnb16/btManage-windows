# btManage for Windows

A companion for Bluetooth devices that lives in the tray. It shows the battery of everything paired with the computer (earbuds with a level for each bud and the case, mice, keyboards, game controllers, headsets), keeps a history of the levels, tells you when a battery runs low, and lets you connect, disconnect and remove devices.

For Windows 10 and Windows 11.

## What it does

- **Tray panel.** Click the icon: the earbuds in use with their three levels, then the other active devices.
- **Window.** Every paired device, grouped by state. For each: batteries, an estimate of the time left, a chart of the last 24 hours, type, address, profiles and signal.
- **Control.** Connect and disconnect audio devices, remove a pairing, switch Bluetooth on and off.
- **Notifications.** When your headphones connect or come near, and when a battery drops to the threshold. Clicking one opens the window on that device.
- **Remembered levels.** A level that is no longer reported, such as the case with both earbuds out, stays on screen with its age.
- **Settings.** Design (Fluent, or Material with opaque tinted surfaces and rounder corners), theme, accent colour (the system's or Bluetooth blue), window backdrop (Acrylic, Mica, Mica Alt; Fluent only), language (English, Russian), start with Windows, updates.
- **Updates.** Checks the GitHub releases of this repository once a day and installs a newer one in place, after checking the download against the SHA-256 GitHub reports.

## Where the levels come from

| Device | Source |
|---|---|
| AirPods, Beats | Bluetooth LE advertisements: left, right, case |
| Fast Pair earbuds | Bluetooth LE advertisements. Not checked against a real device yet |
| Headsets, Bluetooth LE mice, keyboards, remotes, Xbox controllers | The level Windows itself keeps, the one Settings shows |
| Xbox controllers on Bluetooth Classic | The game controller interface of Windows |
| DUALSHOCK 4, DualSense | Their HID report. Off by default, see Settings. Not checked against a real controller yet |

A device that reports no battery to Windows, like many Bluetooth Classic keyboards, is listed without a level. Windows does not tell applications which audio codec is in use, so the codec is not shown.

## Install

From the [releases](../../releases), for x64 or arm64:

| File | What it is |
|---|---|
| `…-setup.exe` | Installer, for the current user, no administrator rights |
| `…-portable.exe` | One self-contained executable |
| `….zip`, `….7z` | The application folder; run `BtManage.exe` |

The files are not code-signed yet, so Windows SmartScreen may ask before the first start.

## Build

Needs the .NET 10 SDK, Rust (stable, MSVC toolchain) and the btManage-core repository checked out next to this one:

```
btManage-core/
btManage-windows/
```

```
dotnet build src/BtManage/BtManage.csproj
dotnet publish src/BtManage/BtManage.csproj -c Release -r win-x64 -p:Platform=x64 -o out/win-x64
```

The build compiles the Rust core as well (`build/RustCore.targets`) and puts `btmanage_ffi.dll` next to the executable. Pass `-p:BtmCoreDir=<path>` if the core lives elsewhere. For ARM64 use `-r win-arm64 -p:Platform=ARM64`. For one executable add `-p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true`.

## Command line

| Switch | Effect |
|---|---|
| `--demo` | Made-up devices instead of the real ones, to look at the interface |
| `--tray` | Start in the tray without opening the window (used by "Start with Windows") |
| `--flyout` | Open the tray panel right after start |
| `--open=<device id>` or `--open=settings` | Open the window there |
| `--update-from=<file>` | Install the application from a release archive on disk |

## CI and releases

`.github/workflows/build.yml` builds x64 and arm64 on every push. A push to `main` or `master` also publishes a release: the version is `<Version>` from `BtManage.csproj`, with a fourth number appended when that version has been released before. Pushing a tag `v1.2.3` releases exactly that version.

Secrets:

| Secret | Purpose |
|---|---|
| `CORE_REPO_TOKEN` | Required. Fine-grained token with read access to the contents of btManage-core, which is private |
| `WINDOWS_CERTIFICATE_BASE64`, `WINDOWS_CERTIFICATE_PASSWORD` | Optional. A code-signing certificate (`.pfx` as base64 text) and its password. With them the executables and the installer are signed |

Pull requests from forks are skipped, since they do not receive secrets.

## Repository map

| Path | What is there |
|---|---|
| `src/BtManage/Bluetooth/` | Sources of data: advertisements, the Windows device tree, controllers, the radio; and actions on devices |
| `src/BtManage/Interop/` | Bindings to the Rust core and the shape of its JSON |
| `src/BtManage/Views/` | Battery cell, meter, device row, history chart |
| `src/BtManage/Updates/` | The updater |
| `installer/` | Inno Setup script |
| `tools/make-icon.ps1` | Draws the icon; `-Sheet` renders all variants to `design/icon-variants.png` |
| `tools/make-strings.ps1` | The English and Russian strings, side by side; generates the `.resw` files |
