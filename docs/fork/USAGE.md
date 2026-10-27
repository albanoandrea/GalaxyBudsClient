# Using Galaxy Buds Client (fork)

This fork of [timschneeb/GalaxyBudsClient](https://github.com/timschneeb/GalaxyBudsClient) controls Samsung Galaxy Buds
from Windows and Android, fully offline. To build it yourself, see [BUILD.md](BUILD.md).

What's different from upstream:

- **Offline only.** No crash reporting, no crowdsourced experiments, no update checks. The Android app has no Internet permission.
- **No firmware updates.** The firmware page is hidden. The firmware version reported by the buds is still shown.
- **Android full version** under its own package, `com.andrea.galaxybudsclient`, so it installs next to the Play Store app.
- **Android background connection**: the buds stay connected after you close the app.
- **Android home-screen widget** with connect/disconnect, noise control and battery.
- **Multi-step ANC level** slider for Buds3 Pro and Buds4 Pro.

The screenshots were taken with Galaxy Buds4 Pro, with the app language set to Italian.

## Windows

### First start

1. Pair the buds with Windows first (Settings > Bluetooth & devices > Add device).
2. Start `GalaxyBudsClient.exe` (portable build) or install it with `GalaxyBudsClient_Setup_*.exe`.
3. The setup wizard lists the paired Galaxy Buds: pick yours. The app connects and opens the home page.

The buds can stay connected to the phone at the same time (on models with multipoint support).

### Pages

The sidebar on the left opens each page. The home page shows battery, temperature and wear state per earbud,
plus shortcuts to the other pages.

| Home | Noise control |
| --- | --- |
| ![Home](images/windows-home.png) | ![Noise control](images/windows-noise-control.png) |

- **Noise control**: Ambient sound, ANC with its level (5 steps on Buds3 Pro / Buds4 Pro), noise control with one earbud,
  and conversation detection.
- **Equalizer**: presets, or Custom for your own 9-band curve, plus left/right balance.
- **360 Audio**: head tracking with live yaw / pitch / roll, recenter, and virtual soundstage width.

| Equalizer | 360 Audio |
| --- | --- |
| ![Equalizer](images/windows-equalizer.png) | ![360 Audio](images/windows-360-audio.png) |

- **Find my earbuds**: makes the buds beep. Take both buds out of your ears first.
- **Touch controls**: lock the touchpad, choose the touch-and-hold action per side and which noise modes it cycles through,
  and turn single, double and triple tap on or off.

| Find my earbuds | Touch controls |
| --- | --- |
| ![Find my earbuds](images/windows-find-buds.png) | ![Touch controls](images/windows-touch-controls.png) |

- **Advanced**: auto play/pause when you wear or remove the buds, seamless connection, call options, keyboard shortcuts
  and Bixby remapping.
- **System**: self test, factory reset, spatial sensor test, debug dumps, usage reports, battery statistics and
  device information. There is no firmware update entry in this fork.

| Advanced | System |
| --- | --- |
| ![Advanced](images/windows-advanced.png) | ![System](images/windows-system.png) |

- **Settings** (gear icon): manage paired devices (Bluetooth icon above the gear), theme, accent colour, language
  and the earbud colour shown in the app.

![Settings](images/windows-settings.png)

## Android

### Install and first start

1. Pair the buds with the phone in Android's Bluetooth settings (or keep the pairing from the Galaxy Wearable app).
2. Copy `com.andrea.galaxybudsclient-Signed.apk` to the phone and open it, or install it over USB with
   `adb install -r com.andrea.galaxybudsclient-Signed.apk`. Android asks you to allow installs from that source.
3. Open **Galaxy Buds Manager**, allow **Nearby devices** (Bluetooth) and **Notifications**, and pick your buds.

### App

The app has the same pages as on Windows. The menu button (top left) opens the page list, paired devices and Settings.

<table>
<tr><th>Home</th><th>Menu</th><th>Noise control</th><th>Noise control, ANC on</th></tr>
<tr>
<td><img src="images/android-home.png" width="200"></td>
<td><img src="images/android-menu.png" width="200"></td>
<td><img src="images/android-noise.png" width="200"></td>
<td><img src="images/android-noise-anc.png" width="200"></td>
</tr>
<tr><th>Equalizer</th><th>Find my earbuds</th><th>Touch controls</th><th>Advanced</th></tr>
<tr>
<td><img src="images/android-eq.png" width="200"></td>
<td><img src="images/android-find.png" width="200"></td>
<td><img src="images/android-touch.png" width="200"></td>
<td><img src="images/android-advanced.png" width="200"></td>
</tr>
<tr><th>System</th><th>Settings</th><th></th><th></th></tr>
<tr>
<td><img src="images/android-system.png" width="200"></td>
<td><img src="images/android-settings.png" width="200"></td>
<td></td><td></td>
</tr>
</table>

### Background connection and notification

Once connected, the buds stay connected when you close the app or swipe it away from recent apps.
A notification shows the battery and a **Disconnect** button; it disappears when you disconnect.

<img src="images/android-notification.png" width="420">

- Connecting from the widget or the notification works even when the app isn't running.
- If the buds go back in the case or are turned off, the connection ends within a few seconds.
- The first connection attempt after the buds wake up sometimes fails; the app retries on its own.

### Home-screen widget

Long-press the home screen > **Widgets** > **Galaxy Buds Manager** > **Buds controls**, then resize it.
The layout follows the size:

| Size | What it shows |
| --- | --- |
| 3×1 | Battery line, Bluetooth button, Off / Ambient sound / Noise cancelling |
| 4×1 | Same as 3×1, plus Adaptive |
| 4×2 | Earbuds name and status, Connect/Disconnect button, L / R / case battery bars, all modes with labels |

- **Bluetooth / Connect button**: connects or disconnects. It always does what it shows, so a stale widget can't do the opposite.
- **Mode buttons**: tap to switch noise control. The selected mode is filled in blue. While disconnected the modes are greyed out
  and a tap opens the app.
- **Adaptive** is shown only for Buds3 Pro and Buds4 Pro, and only from 4×1 up.
- Only the modes your model supports are shown, even before the app has connected once.
- The widget follows the system light/dark theme.

#### 3×1

| State | Widget |
| --- | --- |
| Disconnected | <img src="images/widget-3x1-disconnected.png" width="300"> |
| Connecting | <img src="images/widget-3x1-connecting.png" width="300"> |
| Off | <img src="images/widget-3x1-off.png" width="300"> |
| Ambient sound | <img src="images/widget-3x1-ambient.png" width="300"> |
| Noise cancelling | <img src="images/widget-3x1-anc.png" width="300"> |

#### 4×1

| State | Widget |
| --- | --- |
| Off | <img src="images/widget-4x1-off.png" width="400"> |
| Ambient sound | <img src="images/widget-4x1-ambient.png" width="400"> |
| Adaptive | <img src="images/widget-4x1-adaptive.png" width="400"> |
| Noise cancelling | <img src="images/widget-4x1-anc.png" width="400"> |

#### 4×2

| Disconnected | Off |
| --- | --- |
| <img src="images/widget-4x2-disconnected.png" width="400"> | <img src="images/widget-4x2-off.png" width="400"> |
| **Ambient sound** | **Adaptive** |
| <img src="images/widget-4x2-ambient.png" width="400"> | <img src="images/widget-4x2-adaptive.png" width="400"> |
| **Noise cancelling** | **Dark theme** |
| <img src="images/widget-4x2-anc.png" width="400"> | <img src="images/widget-4x2-dark.png" width="400"> |

## Troubleshooting

- **The widget stays on "Connecting…"**: check that the buds are out of the case and paired in Android's Bluetooth settings,
  then tap the button again.
- **A widget tap opens a permission screen**: allow Nearby devices and Notifications. This happens once after a fresh install.
- **Windows and the phone at the same time**: models with multipoint connect to both. If one side drops,
  reconnect from that app.
- **Updating the Android app fails with a signature error**: the APK was built on another PC. Uninstall first
  (this deletes the app's settings), see [BUILD.md](BUILD.md).
