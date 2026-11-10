# Fork: Android background connection, widgets and tiles

Fork-only feature. Everything lives in new files (this folder, `Resources/**/fork_*`, `ForkManifestOverlay.xml`),
so upstream merges never conflict. The only upstream edit is the one-line backend switch in
`Impl/AndroidPlatformImplCreator.cs` (see below).

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

### Bluetooth backend without polling (`ForkBluetoothService`)

Upstream's Android `Impl/BluetoothService` polls the socket every 50 ms (plus a 2 s check timer), and
`BluetoothImpl.DataConsumerLoop` also polls every 50 ms: ~18 % of a core and ~160 wake-ups/s while connected and idle.
With `ForkPlatformConfig.BlockingAndroidBackend` both creators (`AndroidPlatformImplCreator`, one-line switch, and
`ForkPlatformImplCreator`) use `ForkBluetoothService` instead: per connection one reader thread blocked in `read()`
and one writer thread blocked on a `BlockingCollection`. End of stream / IOException raises `Disconnected` once (not when
we closed the socket ourselves); pending writes are dropped on disconnect. `ForkConfig.EventDrivenDataConsumer`
(`GalaxyBudsClient/Platform/BluetoothImpl.Fork.cs`) makes the consumer wait for data, then batch 50 ms as before.
After upstream merges: diff upstream `Impl/BluetoothService.cs` for fixes worth porting.

### Auto-connect on attach (`BudsAttachReceiver`)

The buds connect to the phone for audio by themselves; this opens the control channel too, with the app not running.

```
 ACL_CONNECTED (manifest receiver, any device) ──► BudsAttachReceiver ──(ActionAutoConnect)──► BudsConnectionService
   setting on? BR/EDR? permissions? MAC == configured?                    foreground at once ("Connecting…"),
   no manual Disconnect < 15 s ago? core not connected/connecting?         wait 4 s, then bootstrap core / connect (3 retries)
```

- **Trigger: `BluetoothDevice.ACTION_ACL_CONNECTED` only.** Exempt from the implicit-broadcast limits, protected (can't be
  spoofed), delivered only with `BLUETOOTH_CONNECT`, and the Bluetooth stack sends it with a temporary FGS-start allowlist,
  which is what lets the receiver start the foreground service from the background (logged if it still throws
  `ForegroundServiceStartNotAllowedException`). Not used: A2DP/headset `CONNECTION_STATE_CHANGED`. They'd say "audio is
  really up", but miss buds used over LE Audio or with media/call audio turned off, and each fires several times per
  connect for every device (each one can cold-start the process). The early-ACL problem is handled by the delay instead.
- **Cheap for other devices**: the receiver reads a SharedPreferences flag, the permission state and settings.json
  (`BudsState.ConfiguredDevice().Mac`, same pick as `DeviceManager`), never the core or Avalonia. Only the service bootstraps
  the core. A cold process start per ACL connect of any device (car, watch) is the remaining cost; nothing runs afterwards.
- **Delay**: ACL comes up before the audio profiles, when RFCOMM tends to fail with "read failed". The service enters the
  foreground immediately, then waits `AttachConnectDelay` (4 s) on a main-thread `Handler` (no wake lock, no polling; the
  FGS keeps the process) before bootstrapping the core: the App's start-up connect (`ForkConfig.ConnectOnStartup`) runs
  as soon as the core is up, so delaying the bootstrap delays that attempt too. This path gets 3 retries instead of 2.
  If the buds' ACL drops again during the delay (`AttachGoneReceiver`, lid opened and closed), it gives up and stops.
  An explicit Connect/Disconnect during the delay cancels it.
- **Manual Disconnect sticks**: after Disconnect (widget/notification) the audio link stays up, so normally no new
  ACL_CONNECTED arrives and nothing reconnects. As a guard against an ACL bounce caused by our own disconnect (e.g. buds
  with no audio profile connected lose the ACL when RFCOMM closes), `ActionDisconnect` stores a timestamp and
  ACL_CONNECTED within `ManualDisconnectWindow` (15 s) is ignored. Any later ACL_CONNECTED means the link really went
  down and up (case, power, range, Bluetooth toggled), and auto-connects again.
- **Setting**: `ForkIntents.AutoConnectOnAttach` / `SetAutoConnectOnAttach` (pref `auto_connect_on_attach` in
  `fork_android`, default on). No UI toggle yet: the only fork-owned UI is the widget/notification; a switch in the app's
  settings page would need an upstream gate plus a bridge from the shared core to Android prefs.

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
  The auto-connect-on-attach path also waits 4 s first (ACL_CONNECTED comes before the audio profiles).
- `BudsAttachReceiver` runs in a fresh process for every device's ACL link: it must never touch the core or Avalonia,
  and must log to logcat directly (`GbFork`), because Serilog is only set up by the bootstrap.
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
- Check merged manifest: `obj/Debug/net10.0-android36.1/android/AndroidManifest.xml` (no `INTERNET`, service + receivers + provider present,
  `BudsAttachReceiver` exported with an `ACL_CONNECTED` filter).

## Test checklist (auto-connect on attach)

Watch `adb logcat -s GbFork GbClient`. Run with the app swiped away / force-stopped unless noted.

- Buds in case, close lid, wait, take them out: `BudsAttachReceiver: ACL_CONNECTED <MAC> transport=1` → `starting auto-connect`
  → `BudsConnectionService: auto-connect in 4 s` → `auto-connecting` → notification/widget "Connected". This is the check
  that the background FGS start is allowed; `cannot start the connection service: ForegroundServiceStartNotAllowedException`
  means it isn't.
- Same with the app open (core running, not connected): connects, UI follows.
- Other device (car, watch, headset) connects: `ignored (not the configured device)`, no notification, no `GbClient` lines.
- Disconnect from widget/notification while the buds stay in the ears: no reconnect, no `ACL_CONNECTED` for the buds.
  Then case → out: auto-connects again.
- Take out and put back within 4 s: `auto-connect cancelled, ACL link went down again`, notification gone.
- Tap Connect on the widget during the 4 s: connects at once, no `auto-connecting` line afterwards (the delayed attempt is cancelled).
- First attempt fails ("read failed"): `connection attempt failed, retrying (2 left)` and it connects on a retry.
- Bluetooth off → on with buds out: auto-connects (counts as a re-attach).
- Fresh install / permission revoked: `ignored (permissions missing ...)`.
