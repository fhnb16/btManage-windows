# btManage for Windows

A tray application that shows the battery levels of wireless earbuds. Click the icon for a panel with the charge of each earbud and the case; a notification appears when a device comes into range and when a battery runs low.

For Windows 10 and Windows 11. Supports AirPods and Beats for now.

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

The build compiles the Rust core as well (`build/RustCore.targets`) and puts `btmanage_ffi.dll` next to the executable. Pass `-p:BtmCoreDir=<path>` if the core lives elsewhere. For ARM64 use `-r win-arm64 -p:Platform=ARM64`.

The published folder is self-contained: copy it anywhere and run `BtManage.exe`.

## Try it without earbuds

```
BtManage.exe --demo --show
```

`--demo` feeds synthetic advertisements through the same path as real ones; `--show` opens the panel at start.

## How it works

`BluetoothLEAdvertisementWatcher` listens passively for advertisements. Their manufacturer data goes to the tracker in the Rust core, which returns the state as JSON whenever something visible changes. Levels come in steps of 10 %, and only while the earbuds advertise: worn, or in an open case.

Windows puts new tray icons into the overflow. Drag the icon onto the taskbar to keep it visible.

## CI

`.github/workflows/build.yml` publishes x64 and ARM64 builds as artifacts. Because btManage-core is private, the workflow needs a repository secret `CORE_REPO_TOKEN`: a fine-grained personal access token with read access to the contents of btManage-core. Pull requests from forks are skipped, since they do not receive secrets.

## Icon

`tools/make-icon.ps1` draws `src/BtManage/Assets/app.ico`. Run it again only when the mark changes.
