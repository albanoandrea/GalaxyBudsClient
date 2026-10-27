using Android.Content;
using GalaxyBudsClient.Android.Impl;
using GalaxyBudsClient.Platform;
using GalaxyBudsClient.Platform.Interfaces;
using Serilog;

namespace GalaxyBudsClient.Android.Fork;

/// <summary>
/// Same as <see cref="AndroidPlatformImplCreator"/>, but meant for the application context:
/// it doesn't keep an activity alive and opens links with <see cref="ActivityFlags.NewTask"/>.
/// </summary>
internal class ForkPlatformImplCreator(Context appContext) : IPlatformImplCreator
{
    public IDesktopServices CreateDesktopServices() => new ForkDesktopServices(appContext);
    public IBluetoothService CreateBluetoothService() => new BluetoothService(appContext);
    public IHotkeyBroadcast? CreateHotkeyBroadcast() => null;
    public IHotkeyReceiver? CreateHotkeyReceiver() => null;
    public IMediaKeyRemote? CreateMediaKeyRemote() => null;
    public INotificationListener? CreateNotificationListener() => null;
    public IOfficialAppDetector CreateOfficialAppDetector() => new OfficialAppDetector(appContext);
}

internal class ForkDesktopServices(Context context) : BaseDesktopServices
{
    public override bool IsAutoStartEnabled { get; set; } = false;

    public override void OpenUri(string uri)
    {
        try
        {
            var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(uri));
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "ForkDesktopServices: Cannot open link {Uri}", uri);
        }
    }
}
