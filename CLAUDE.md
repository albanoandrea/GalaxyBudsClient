# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## This is a fork — stay merge-compatible with upstream

This repo (`origin` = `albanoandrea/GalaxyBudsClient`) is a fork of upstream `timschneeb/GalaxyBudsClient`. We want to keep pulling future upstream changes, so every fork-specific change must minimize merge conflicts:

- **Gate, don't delete.** Do not remove, rename, move, or reformat upstream files/code. Disable behavior with the smallest possible edit at the entry point (ideally one line), so upstream edits to the disabled code still merge cleanly.
- Put fork-specific logic in **new files** where possible (new files never conflict). Switches live in [GalaxyBudsClient/ForkConfig.cs](GalaxyBudsClient/ForkConfig.cs) (root `GalaxyBudsClient` namespace, so no extra `using` is needed elsewhere).
- Avoid adding `using`/`xmlns` lines at the top of upstream files; prefer local/qualified references.
- Match upstream style; no drive-by refactors or cleanups in upstream code.

To sync: `git remote add upstream https://github.com/timschneeb/GalaxyBudsClient.git` (once), then `git fetch upstream && git merge upstream/master`. After merging, re-check that no new network calls or firmware entry points were introduced (see below).

## Fork-specific constraints

- **No firmware updates from the app.** Do not add, restore, or extend firmware flashing/downgrading. Disabled via `ForkConfig.FirmwareUpdatesEnabled`:
  - `IDeviceSpec.Supports()` returns `false` for `Features.FirmwareUpdates` (checked before the translator "grant all features" override). This hides the firmware entry in `SystemPage`, which is the only path to `FirmwarePage` → `FirmwareUpdateDialog` → `FirmwareTransferManager`.
  - The upstream firmware code (`Model/Firmware/*`, `Message/FirmwareTransferManager.cs`, FOTA decoders) stays in place but unreachable. Showing the firmware *version* reported by the buds is fine.
- **No backend, no automatic updates, no outbound network calls.** The app must work fully offline. Disabled via `ForkConfig.OnlineServicesEnabled`:
  - Sentry crash reporting: `Program.cs` never calls `CrashReports.SetupCrashHandler()`, so `SentrySdk.*` calls elsewhere are no-ops.
  - Crowdsourced experiments (`crowdsourcing.timschneeberger.me`): `ExperimentClient.ScanForExperiments()` returns early, so no experiment is ever fetched, run, or reported.
  - The "Crowdsourcing" section (experiments + crash report toggles) is hidden in `SettingsPage.axaml` with a literal `IsVisible="False"` (Avalonia only allows `xmlns` on the root element, so it can't bind to `ForkConfig` without touching the file header).
  - Firmware download server (`fw.timschneeberger.me`, `FirmwareRemoteClient`): only reachable from the disabled firmware page.
  - Never introduce new HTTP clients, telemetry, or update checks. Opening external links in the browser on explicit user action (e.g. `DesktopServices.OpenUri`) is fine.
- **Android build is the full version under our own package id.** Overrides live only in new files in `GalaxyBudsClient.Android/` (the upstream csproj and `Properties/AndroidManifest.xml` are untouched):
  - `Directory.Build.props` sets `NoDemo=true` (full icons/branding).
  - `Directory.Build.targets` sets `ApplicationId=com.andrea.galaxybudsclient` and strips the `RequestFullVersion` define, so the "Open on Google Play" upsell in `Dialogs.RequireFullVersion()` is never compiled in.
  - `ForkManifestOverlay.xml` (an `AndroidManifestOverlay`) removes `android.permission.INTERNET` from the merged manifest. Verify in `obj/Debug/net10.0-android36.1/android/AndroidManifest.xml` after upstream merges.
  - Background connection service + home-screen widgets live in `GalaxyBudsClient.Android/Fork/` (design and pitfalls in its `README.md`). After upstream merges, re-check that `ForkAndroidBootstrap` still mirrors `MainActivity.CustomizeAppBuilder`; after Avalonia upgrades, re-check the reflection on `AvaloniaMainActivity.Lifetime`/`SingleViewLifetime`.

## Build & test

.NET SDK pinned in [global.json](global.json) (10.0.3xx feature band). Restore must use the repo's NuGet config.

On Windows, the fork-specific scripts in [setup/](setup/) wrap everything:

```powershell
# Fresh machine: per-user .NET SDK (no admin) + NuGet restore; -WithInstaller also installs NSIS
powershell -ExecutionPolicy Bypass -File setup\setup-windows.ps1 -WithInstaller

# -Mode build | test | publish | installer   (publish → bin\win-x64, installer → GalaxyBudsClient_Setup_*.exe in repo root)
powershell -ExecutionPolicy Bypass -File setup\build-windows.ps1 -Mode publish -Runtime win-x64

# Android: -WithAndroid installs the android workload, SDK platform (into $env:ANDROID_HOME) and a private JDK
# in %LOCALAPPDATA%\Android\jdk (.NET for Android rejects JDK 25). android → bin\android\*-Signed.apk,
# android-install also runs `adb install -r`. -AndroidRuntime android-arm64 builds one ABI (~67 MB vs ~270 MB for all four);
# it maps to -p:ForkAndroidRuntime, because `-r` / a global RuntimeIdentifiers don't work here.
powershell -ExecutionPolicy Bypass -File setup\setup-windows.ps1 -WithAndroid
powershell -ExecutionPolicy Bypass -File setup\build-windows.ps1 -Mode android-install -AndroidRuntime android-arm64
```

The Android build mirrors upstream `GalaxyBudsClient.Android/build.sh`: `-c Debug` with `EmbedAssembliesIntoApk=true`/`NotDebuggable=true`, and `-p:IsAndroid=true` passed globally (it doesn't propagate to project references). It's signed with the local .NET debug keystore, so only builds from the same machine install over each other.

Raw commands:

```sh
# Restore + build (TargetFramework is auto-selected from the host OS, see GalaxyBudsClient.csproj)
dotnet restore --configfile GalaxyBudsClient/nuget.config GalaxyBudsClient/GalaxyBudsClient.csproj
dotnet build GalaxyBudsClient/GalaxyBudsClient.csproj

# Run
dotnet run --project GalaxyBudsClient/GalaxyBudsClient.csproj

# Release publish (Windows, as in CI)
dotnet publish -f net10.0-windows10.0.19041 -r win-x64 -c Release -p:PublishSingleFile=true --self-contained true GalaxyBudsClient/GalaxyBudsClient.csproj

# Tests (NUnit + FluentAssertions)
dotnet test GalaxyBudsClient.Tests
dotnet test GalaxyBudsClient.Tests --filter "FullyQualifiedName~Buds2Pro"   # single fixture/namespace
```

Target frameworks: Windows → `net10.0-windows10.0.19041`; macOS → `net10.0-macos` only with `-p:UseMacOSSDK=true` (needs `dotnet workload install macos`); Linux/other → `net10.0`. Platform `#if Windows / Linux / OSX / Android` symbols come from `Platform.props`. See `.github/workflows/compile-*.yml` for all per-platform publish commands.

## Architecture

Avalonia (FluentAvalonia + ReactiveUI) desktop app that talks to Galaxy Buds over Bluetooth RFCOMM/SPP using Samsung's proprietary binary protocol (notes in `GalaxyBudsRFCommProtocol.md`, `Galaxy Buds Plus RFComm Protocol Notes.md`, `GalaxyBudsPlus_HiddenDebugFeatures.md`).

**Projects**
- `GalaxyBudsClient` – main app (UI, protocol, settings, CLI). Also used as a library by `GalaxyBudsClient.Android`.
- `GalaxyBudsClient.Platform` – platform-agnostic interfaces (`IBluetoothService`, `IDesktopServices`, hotkeys, media keys) plus stubs.
- `GalaxyBudsClient.Platform.{Windows,WindowsRT,Linux,OSX}` – per-OS implementations (Linux uses BlueZ via `ThePBone.BlueZNet`, Windows uses `InTheHand.Net.Personal.Core` or WinRT). `Platform/PlatformImpl.cs` picks the `IPlatformImplCreator` at startup via compile-time symbols.
- `GalaxyBudsClient.Generators` – Roslyn source generators (see below).
- `GalaxyBudsClient.Tests` – decoder tests driven by captured binary payloads in `TestData/`, one folder per device model.

**Message pipeline** (`Message/`)
- `Platform/BluetoothImpl.cs` owns the connection; raw bytes are framed/parsed into `SppMessage` (`SppMessage.cs`, IDs in `SppMessageEnums.cs` as `MsgIds`).
- Incoming messages are decoded by classes in `Message/Decoder/` marked `[MessageDecoder(MsgIds.X)]`; outgoing commands use `Message/Encoder/`. The decoder/encoder lookup factory is **source-generated** (`Generators/Messages/MessageHandlerFactoryGenerator.cs`) — adding a decoder only requires the attribute.
- `SppMessageReceiver` dispatches decoded messages as events; `DeviceMessageCache` keeps the latest state that UI view models read.

**Device capabilities** – each model has an `IDeviceSpec` in `Model/Specifications/` mapping `Features` to an optional `FeatureRule(minRevision)`. UI and logic gate behavior via these features rather than checking model enums directly.

**Generated code** – enums with `[CompiledEnum]` get fast extension methods; localization keys (`GalaxyBudsClient.Generated.I18N.Keys/Strings`) are generated from `i18n/en.axaml`. Add new strings to `en.axaml` first; other `i18n/*.axaml` files are maintained by translators (validated by `translation-validate.yml`).

**UI** – `Interface/Pages/*` (views) + `Interface/ViewModels/Pages/*` (ReactiveUI view models, `[Reactive]` source-generated properties). Settings live in `Model/Config/SettingsData.cs` (persisted JSON); battery history uses EF Core SQLite (`Model/Database`, `Migrations/`). `Cli/` provides a command-line/IPC mode when the app is launched with arguments.
