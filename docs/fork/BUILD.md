# Building the fork

Short version. Everything runs from the repo root in PowerShell on Windows, without admin rights.
See [CLAUDE.md](../../CLAUDE.md) for the raw `dotnet` commands and other platforms.

## 1. One-time setup

```powershell
# .NET SDK (per user) + NuGet restore. -WithInstaller also installs NSIS for the Windows installer.
powershell -ExecutionPolicy Bypass -File setup\setup-windows.ps1 -WithInstaller

# Android only: android workload, Android SDK platform and a private JDK
powershell -ExecutionPolicy Bypass -File setup\setup-windows.ps1 -WithAndroid
```

## 2. Windows

```powershell
# Portable build -> bin\win-x64\GalaxyBudsClient.exe
powershell -ExecutionPolicy Bypass -File setup\build-windows.ps1 -Mode publish -Runtime win-x64

# Installer -> GalaxyBudsClient_Setup_*.exe in the repo root
powershell -ExecutionPolicy Bypass -File setup\build-windows.ps1 -Mode installer
```

Other modes: `build` (compile only) and `test` (run the unit tests).

## 3. Android

```powershell
# APK -> bin\android\com.andrea.galaxybudsclient-Signed.apk
powershell -ExecutionPolicy Bypass -File setup\build-windows.ps1 -Mode android -AndroidRuntime android-arm64

# Same, then install on the phone connected over adb (USB debugging on)
powershell -ExecutionPolicy Bypass -File setup\build-windows.ps1 -Mode android-install -AndroidRuntime android-arm64
```

- `-AndroidRuntime` builds a single ABI (about 67 MB instead of 270 MB for all four). Use `android-arm64`
  for phones and `android-x64` for the emulator. Leave it out to build all ABIs.
- With more than one device attached, install by hand: `adb -s <serial> install -r bin\android\com.andrea.galaxybudsclient-Signed.apk`.
- The APK is signed with this machine's .NET debug key. An APK built on another PC can't update it:
  uninstall first (this deletes the app's settings).
