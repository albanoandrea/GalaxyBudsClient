using System.Diagnostics.CodeAnalysis;
using Android.Content;
using GalaxyBudsClient.Platform.Interfaces;

namespace GalaxyBudsClient.Android.Impl;

[SuppressMessage("ReSharper", "UnusedType.Global")]
public class AndroidPlatformImplCreator(Context context) : IPlatformImplCreator
{
    public IDesktopServices CreateDesktopServices() => new DesktopServices(context);
    public IBluetoothService CreateBluetoothService() => GalaxyBudsClient.Platform.ForkPlatformConfig.BlockingAndroidBackend ? new GalaxyBudsClient.Android.Fork.ForkBluetoothService(context) : new BluetoothService(context);
    public IHotkeyBroadcast? CreateHotkeyBroadcast() => null;
    public IHotkeyReceiver? CreateHotkeyReceiver() => null;
    public IMediaKeyRemote? CreateMediaKeyRemote() => null;
    public INotificationListener? CreateNotificationListener() => null;
    public IOfficialAppDetector CreateOfficialAppDetector() => new OfficialAppDetector(context);
}