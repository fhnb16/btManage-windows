# Working in btManage-windows

Guidance for anyone, human or AI, changing this repository.

## What this is

The Windows client of btManage: WinUI 3 on .NET 10, unpackaged and self-contained. Parsing, the device list, history and the update check live in the Rust core (`../btManage-core`); this repository talks to Bluetooth and draws the interface.

## Rules

- **Logic that every platform needs goes into the core**, not here. If Linux or Android would want it too, it is not a C# change.
- **Tooling is PowerShell, C# or Rust.** No Python or Node, neither as scripts nor as build steps.
- **Do not commit or push unless asked.** The maintainer does that.
- **Raise the version with every change that ships**, as `major.minor.patch`: patch for a fix, minor for a new capability, major for a break. The version is `<Version>` in `src/BtManage/BtManage.csproj`; keep `assemblyIdentity` in `app.manifest` in step. CI adds a fourth number by itself only when the same version is built again.
- **Strings** are edited in `tools/make-strings.ps1`, English and Russian together, then `pwsh tools/make-strings.ps1`. Never edit the `.resw` files by hand.
- **The icon** is drawn by `tools/make-icon.ps1`. The tray glyph in `Tray/TrayGlyph.cs` uses the same numbers; change both.
- **Release file names are a contract** with the updater (`Updates/Updater.cs`): `btManage-windows-<arch>.zip` and `btManage-windows-<arch>-portable.exe`. Renaming them strands installed copies.
- **Anything that changes a real device** (connect, disconnect, unpair, the Bluetooth switch) is not to be exercised in a test run without the user asking. Check such code up to the call, not through it.
- Mark what has not been tried on real hardware, in the code comment and in `README.md`.

## Interface

The look is the system's: Segoe UI Variable, Segoe Fluent Icons, Acrylic or Mica, the accent colour. Keep it that way.

- Spacing on a 4 px grid. Equal things get equal sizes.
- One accent, kept for the charge meters, the leading line of the chart and the selection. The warning colour only for a low battery.
- Numbers use tabular figures and one format (`DeviceText.Percent`).
- Sentence case. No exclamation marks, no trailing period on labels, no emoji.
- An unknown value is left out, not shown as a placeholder. A remembered value is dimmed and carries its age.
- No decoration: no gradients, glows or looping animation.
- The ten-segment meter is the signature: earbuds report their charge in steps of 10 %.

There is a second design, Material, chosen in the settings. It is the same interface with resources replaced (`Appearance.Apply`): surfaces tinted with the accent, translucent over Mica or Acrylic and opaque with the solid backdrop, cards without a border, larger corner radii. Use theme resources for surfaces, strokes and radii (`CardBackgroundFillColorDefaultBrush`, `OverlayCornerRadius` and so on), never literal values, or the Material design will not reach your element.

Design, theme, accent and backdrop apply while the application runs: `Appearance.Apply` sets the resources of the application, `Appearance.ApplyWindow` sets the theme on the content of a window and turns it over once so that every theme resource is looked up again. Only the language restarts the application.

So never take a theme brush from `Application.Current.Resources` in code: such a brush keeps the theme of the application, not the one the user chose, and does not change with it. Give an element made in code one of the `Btm…Style` styles of `App.xaml` with `Appearance.Style`, whose theme resources follow the element; add a style there when none fits.

## Checking a change

```
dotnet build src/BtManage/BtManage.csproj -c Release
```

Warnings are errors. Then look at it:

```
BtManage.exe --demo                       the window, on made-up devices
BtManage.exe --demo --flyout              the tray panel
BtManage.exe --demo --open=demo-earbuds   one device
BtManage.exe --demo --open=settings       the settings
```

Demo mode uses its own settings file (`%LOCALAPPDATA%\btManage\settings.demo.json`) and remembers no levels, so it is safe next to a real installation. A screenshot of a window that is not in front needs `PrintWindow` with `PW_RENDERFULLCONTENT`; copying from the screen picks up whatever covers it.

Only one instance runs per session. Stop the running one before starting another, or the second start just brings the first window up.

## Where things go

- A new source of battery levels: a class in `Bluetooth/` that feeds `DeviceMonitor`, added in `App.OnLaunched`.
- A new field from the core: `Interop/Snapshot.cs`, then where it is shown.
- A new setting: `AppSettings.cs`, a row in `MainWindow.BuildSettings`, strings in `tools/make-strings.ps1`.
- A new action on a device: `Bluetooth/DeviceActions.cs`, then the header of the device page in `MainWindow`.
