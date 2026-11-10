using System;
using Android.App;
using Android.Bluetooth;
using Android.Content;

namespace GalaxyBudsClient.Android.Fork;

/// <summary>
/// Opens the control channel by itself when the configured buds attach to the phone (taken out of the case,
/// switched on, back in range), also when the app isn't running. See Fork/README.md ("Auto-connect on attach").
/// </summary>
/// <remarks>
/// Runs for every ACL link of every device, often in a fresh process before Avalonia is set up: it must stay cheap
/// and must not touch any Avalonia type or the core's Settings (see README pitfalls). Only the service bootstraps the core.
/// Exported because the sender is the Bluetooth process; ACL_CONNECTED is a protected broadcast (can't be spoofed) and
/// is only delivered to apps holding BLUETOOTH_CONNECT (API 31+).
/// </remarks>
[BroadcastReceiver(
    Name = "com.andrea.galaxybudsclient.fork.BudsAttachReceiver",
    Exported = true)]
[IntentFilter([BluetoothDevice.ActionAclConnected])]
internal class BudsAttachReceiver : BroadcastReceiver
{
    private const string Tag = "GbFork";
    // BluetoothDevice.EXTRA_TRANSPORT / TRANSPORT_LE (API 33+; absent before)
    private const string ExtraTransport = "android.bluetooth.device.extra.TRANSPORT";
    private const int TransportLe = 2;

    /// <summary>
    /// After a manual Disconnect, an ACL_CONNECTED within this window is treated as a bounce caused by our own disconnect
    /// (not a re-attach) and ignored. Normally the audio link stays up and no ACL_CONNECTED follows at all.
    /// </summary>
    public static readonly TimeSpan ManualDisconnectWindow = TimeSpan.FromSeconds(15);

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || intent?.Action != BluetoothDevice.ActionAclConnected)
            return;
        try
        {
            Evaluate(context, intent);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn(Tag, $"BudsAttachReceiver: failed: {ex}");
        }
    }

    private static void Evaluate(Context context, Intent intent)
    {
        var address = DeviceAddress(intent);
        var transport = intent.GetIntExtra(ExtraTransport, -1);
        global::Android.Util.Log.Info(Tag, $"BudsAttachReceiver: ACL_CONNECTED {address ?? "<no device>"} transport={transport}");

        // Cheapest checks first: most broadcasts are other devices (car, watch, ...)
        if (!ForkIntents.AutoConnectOnAttach(context))
        {
            Skip("auto-connect on attach is off");
            return;
        }
        if (transport == TransportLe)
        {
            // RFCOMM needs the BR/EDR link; an LE link (e.g. LE Audio/BLE side channel) is not the attach we want
            Skip("LE transport");
            return;
        }
        if (!ForkIntents.HasBluetoothPermission(context) || ForkIntents.NeedsPermissionScreen(context))
        {
            Skip("permissions missing (open the widget once to grant them)");
            return;
        }

        var configured = BudsState.ConfiguredDevice().Mac;
        if (address == null || configured == null || !string.Equals(address, configured, StringComparison.OrdinalIgnoreCase))
        {
            Skip(configured == null ? "no device configured in the app" : "not the configured device");
            return;
        }
        if (ForkIntents.IsRecentManualDisconnect(context, ManualDisconnectWindow))
        {
            Skip("user disconnected manually just now");
            return;
        }

        // Only meaningful if the core already runs in this process (UI open, or the service is up)
        var state = BudsState.Current;
        if (state.IsConnected || state.IsConnecting)
        {
            Skip($"already {(state.IsConnected ? "connected" : "connecting")}");
            return;
        }

        try
        {
            // Allowed from the background: the Bluetooth stack delivers this broadcast with a temporary FGS allowlist
            ForkIntents.StartService(context, ForkIntents.ActionAutoConnect);
            global::Android.Util.Log.Info(Tag, $"BudsAttachReceiver: configured buds attached, starting auto-connect (service running: {BudsConnectionService.IsRunning})");
        }
        catch (Exception ex)
        {
            // e.g. ForegroundServiceStartNotAllowedException (API 31+) if the allowlist didn't apply
            global::Android.Util.Log.Warn(Tag, $"BudsAttachReceiver: cannot start the connection service: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Skip(string reason) =>
        global::Android.Util.Log.Info(Tag, $"BudsAttachReceiver: ignored ({reason})");

    private static string? DeviceAddress(Intent intent)
    {
        var device = OperatingSystem.IsAndroidVersionAtLeast(33)
            ? (BluetoothDevice?)intent.GetParcelableExtra(BluetoothDevice.ExtraDevice, Java.Lang.Class.FromType(typeof(BluetoothDevice)))
#pragma warning disable CA1422
            : (BluetoothDevice?)intent.GetParcelableExtra(BluetoothDevice.ExtraDevice);
#pragma warning restore CA1422
        return device?.Address;
    }
}
