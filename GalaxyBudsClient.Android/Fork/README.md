# Fork: Android background connection, widgets and tiles

Fork-only feature. Everything lives in new files (this folder, `Resources/**/fork_*`, `ForkManifestOverlay.xml`),
so upstream merges never conflict. No upstream file is edited.

## Architecture

```
 Widgets / QS tile / notification  ──(PendingIntent → service actions)──►  BudsConnectionService (FGS, connectedDevice)
          ▲                                                                        │
          └──── WidgetRenderer.UpdateAll() ◄── BudsState.Changed ◄─────────────────┤
                                                                                   ▼
                                                  BluetoothImpl.Instance / SppMessageReceiver / DeviceMessageCache
                                                  (same core + same Avalonia App instance the UI uses)
```

### Starting the core without the UI (`ForkAndroidBootstrap`)

The core posts all events through `Avalonia.Threading.Dispatcher.UIThread`. If that is touched before Avalonia is set up,
Avalonia caches a `NullDispatcherImpl` for the whole process and nothing ever gets delivered. So a service that cold-starts
the process must set Avalonia up itself, exactly like `MainActivity` does:

1. `Program.Startup(...)` + `PlatformImpl.InjectExternalBackend(...)` (application context, `NewTask`-safe `OpenUri`)
2. `AppBuilder.Configure<App>().UseAndroid().WithInterFont().LogToTrace().UseReactiveUI()` (mirror of `MainActivity.CustomizeAppBuilder`)
   `.SetupWithLifetime(new SingleViewLifetime())`. `App.OnFrameworkInitializationCompleted` then runs as usual
   (auto-connect, `MainView`, ManagerInfo replies, EQ re-push, ...).
3. Store that lifetime in the private static `AvaloniaMainActivity.Lifetime` (reflection). When the user later opens the app,
   `AvaloniaMainActivity.InitializeAvaloniaView` takes its "reuse" branch: no second `Setup`, no second `CustomizeAppBuilder`,
   so no `BluetoothImpl.Reallocate()` dropping the live connection.

If the activity started first, `Application.Current` is already set and the bootstrap does nothing.

**Re-check after every Avalonia upgrade** (`AvaloniaMainActivity.Lifetime`, `SingleViewLifetime`) and after every upstream merge
(`MainActivity.CustomizeAppBuilder` builder chain). Avalonia 12 has `AvaloniaAndroidApplication<TApp>`, which makes the reflection
unnecessary.

### Process hook (`ForkStartupProvider`)

A `ContentProvider` is created at process start, before any activity. It registers activity lifecycle callbacks; once
`MainActivity` is up it subscribes to `BluetoothImpl.Instance.Connected` and starts the service, so a connection made from
the UI also survives closing the app and keeps widgets updated.

## Phases

1. **Foundation (done)**: bootstrap, `BudsConnectionService` (foreground, notification with Connect/Disconnect),
   `BudsState` snapshot (connection + battery), link-loss detection (ACL / adapter off), connect retries,
   process hook, permission trampoline, Connect widget (since replaced by the controls widget).
2. **Controls widget (done)**: one responsive widget (`ControlsWidgetProvider`); only the 2-row layout shows the device name. The layout is
   picked by size (Android 12+: a size → RemoteViews map, the launcher switches while resizing; older: from the
   widget options in `OnAppWidgetOptionsChanged`):
   - **3×1** (`fork_widget_controls_compact`): tiny battery/status line, Bluetooth icon toggle, Off / Ambient / ANC icons.
   - **4×1**: same, plus Adaptive.
   - **2 rows** (`fork_widget_controls`): device name + status, Connect/Disconnect pill, L / R / case battery bars,
     labelled mode circles on a track.

   Modes are gated by `Features.NoiseControl` / `AmbientSound` / `Anc`; legacy models use `SET_AMBIENT_MODE` /
   `SET_NOISE_REDUCTION`. Adaptive (`NOISE_CONTROLS` = 3) is shown for Buds3 Pro / Buds4 Pro (fork-side check in
   `BudsState.ModeSupportFor`, because enabling `Features.NoiseControlAdaptive` in the upstream specs would also change
   ESU touch-option parsing). Before the core runs, the model is read from settings.json (`BudsState.ConfiguredDevice`,
   spec `Rules` read directly: `IDeviceSpec.Supports` touches an Avalonia view), so the columns are right from the start.
   State comes from the ESU, `NoiseControlUpdateResponse`, `Anc/AmbientEnabledUpdateResponse` and `NOISE_CONTROLS` acks;
   the app UI follows widget changes through the same ack. Mode taps go to the service (`ActionSetNoiseMode`).
3. **Next**: QS tile, then more toggles as needed: touchpad lock, EQ preset, ambient volume, find my buds, conversation detect.

### Pitfalls found while testing

- **Never touch an Avalonia type before setup** (not even `Application.Current`): it caches a `NullDispatcherImpl`,
  giving a white UI and no core events (`Connected`, battery). `ForkAndroidBootstrap` logs the dispatcher it ends up with.
- The first RFCOMM connect after the buds (re)attach often fails with "read failed"; the core doesn't retry, the service does.
- Widget buttons send the action shown on their label (never a toggle), so a stale widget can't do the opposite.
- RemoteViews size maps: the launcher picks the entry with the **largest area** that fits, so every key needs a real
  width *and* height (a 0 in either makes all areas equal). `Button` gets an elevation shadow; use a `TextView`.
- Design iteration works well on the `Pixel_8_API_37` emulator (`-AndroidRuntime android-x64`): a temporary exported
  receiver that injects a fake `BudsSnapshot` over `adb shell am broadcast` lets you style every state without earbuds.

## Test checklist (phase 1)

- Cold start from widget (app swiped away / force-stopped): tap → notification "Connecting…" → "Connected", widget shows battery.
- Open the app while connected from the widget: UI shows live state, connection is not dropped.
- Swipe the app from recents while connected: notification and connection stay.
- Disconnect from widget / notification: buds disconnect, notification disappears, widget shows "Disconnected".
- Turn buds off / put them in the case: widget switches to "Disconnected" within a few seconds.
- Connect from the app UI: notification appears, widget updates.
- Fresh install without permissions: widget tap opens the permission screen first.
- Check merged manifest: `obj/Debug/net10.0-android36.1/android/AndroidManifest.xml` (no `INTERNET`, service + receiver + provider present).
