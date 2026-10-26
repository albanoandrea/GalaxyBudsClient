using System;
using System.Threading.Tasks;
using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using GalaxyBudsClient.Platform;
using Serilog;

namespace GalaxyBudsClient.Android.Fork;

/// <summary>
/// Foreground service that keeps the Bluetooth connection (and the shared core) alive without the UI.
/// It runs only while connecting/connected and stops itself once the buds are disconnected.
/// </summary>
[Service(
    Name = "com.andrea.galaxybudsclient.fork.BudsConnectionService",
    Exported = false,
    // shortService: only used to satisfy startForegroundService() when we have to bail out without BLUETOOTH_CONNECT
    ForegroundServiceType = ForegroundService.TypeConnectedDevice | ForegroundService.TypeShortService)]
internal class BudsConnectionService : Service
{
    private const int NotificationId = 1001;
    private const string ChannelId = "fork_connection";
    private const int TypeConnectedDevice = 0x10; // ForegroundService.TypeConnectedDevice (API 29+)
    private const int TypeShortService = 0x800;   // ForegroundService.TypeShortService (API 34+)
    // How long a disconnect may last (e.g. multipoint patcher / auto-reconnect) before the service stops
    private static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(5);
    private static readonly Java.Lang.String StopToken = new("stop");
    // The first RFCOMM connect after the buds (re)attach often fails with "read failed"; the core doesn't retry
    private const int MaxConnectRetries = 2;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private static readonly Java.Lang.String RetryToken = new("retry");

    public static bool IsRunning { get; private set; }

    private readonly Handler _handler = new(Looper.MainLooper!);
    private LinkReceiver? _linkReceiver;
    private bool _inForeground;
    private int _lastStartId;
    private int _retriesLeft;
    private bool _wasConnecting;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        IsRunning = true;
        CreateChannel();
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        _lastStartId = startId;
        var action = intent?.Action ?? ForkIntents.ActionAttach;

        // startForeground must come first (within seconds of startForegroundService), before the slow core bootstrap
        if (!TryEnterForeground())
        {
            BailOut();
            return StartCommandResult.NotSticky;
        }

        // A stale widget/notification "Disconnect" in a fresh process: nothing to disconnect, just redraw
        if (action != ForkIntents.ActionConnect && !ForkAndroidBootstrap.IsInitialized)
        {
            WidgetRenderer.UpdateAll(this);
            StopService();
            return StartCommandResult.NotSticky;
        }

        try
        {
            // Also subscribes BudsState before the App's auto-connect starts
            ForkAndroidBootstrap.EnsureInitialized(this);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "BudsConnectionService: core bootstrap failed");
            StopService();
            return StartCommandResult.NotSticky;
        }

        BudsState.Changed -= OnStateChanged;
        BudsState.Changed += OnStateChanged;
        BudsState.EnsureInitialized(this);
        RegisterLinkReceiver();

        Log.Debug("BudsConnectionService: {Action}", action);
        HandleAction(action);
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        IsRunning = false;
        _handler.RemoveCallbacksAndMessages(null);
        BudsState.Changed -= OnStateChanged;
        if (_linkReceiver != null)
        {
            UnregisterReceiver(_linkReceiver);
            _linkReceiver = null;
        }
        base.OnDestroy();
    }

    private void HandleAction(string action)
    {
        var state = BudsState.Current;
        if (!state.HasValidDevice)
        {
            Log.Warning("BudsConnectionService: no device registered, set one up in the app first");
            StopService();
            return;
        }

        switch (action)
        {
            case ForkIntents.ActionDisconnect:
                _retriesLeft = 0;
                _handler.RemoveCallbacksAndMessages(RetryToken);
                _ = DisconnectAsync();
                break;
            case ForkIntents.ActionConnect:
                _retriesLeft = MaxConnectRetries;
                _wasConnecting = state.IsConnecting;
                // On a cold start the App's auto-connect is already running (BudsState shows it as connecting)
                if (!state.IsConnected && !state.IsConnecting)
                    _ = ConnectAsync();
                break;
            default: // ActionAttach
                ScheduleStopIfIdle();
                break;
        }
    }

    private static async Task ConnectAsync()
    {
        BudsState.SetConnecting(true);
        try
        {
            if (!await Task.Run(() => BluetoothImpl.Instance.ConnectAsync()))
                BudsState.SetConnecting(false);
        }
        catch (Exception ex)
        {
            // e.g. Java.Lang.SecurityException if the Bluetooth permission was revoked
            Log.Error(ex, "BudsConnectionService: connect failed");
            BudsState.SetConnecting(false);
        }
    }

    private async Task DisconnectAsync()
    {
        try
        {
            await BluetoothImpl.Instance.DisconnectAsync();
            // A connect that was just completing raises Connected ~150 ms later; disconnecting doesn't cancel that
            await Task.Delay(500);
            if (BluetoothImpl.Instance.IsConnected)
                await BluetoothImpl.Instance.DisconnectAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "BudsConnectionService: disconnect failed");
        }
        BudsState.SetConnecting(false);
        ScheduleStopIfIdle(TimeSpan.Zero);
    }

    private void OnStateChanged(BudsSnapshot state)
    {
        UpdateNotification(state);
        var attemptFailed = _wasConnecting && !state.IsConnecting && !state.IsConnected;
        _wasConnecting = state.IsConnecting;

        if (state.IsConnected)
            _retriesLeft = 0;

        if (state.IsConnected || state.IsConnecting)
        {
            _handler.RemoveCallbacksAndMessages(StopToken);
        }
        else if (attemptFailed && _retriesLeft > 0)
        {
            // The user asked to connect: retry instead of stopping (the stop grace is longer than the retry delay)
            _retriesLeft--;
            Log.Information("BudsConnectionService: connection attempt failed, retrying ({Left} left)", _retriesLeft);
            _handler.RemoveCallbacksAndMessages(RetryToken);
            _handler.PostAtTime(() =>
            {
                var current = BudsState.Current;
                if (!current.IsConnected && !current.IsConnecting)
                    _ = ConnectAsync();
            }, RetryToken, SystemClock.UptimeMillis() + (long)RetryDelay.TotalMilliseconds);
            ScheduleStopIfIdle();
        }
        else
        {
            ScheduleStopIfIdle();
        }
    }

    private void ScheduleStopIfIdle() => ScheduleStopIfIdle(StopGrace);

    private void ScheduleStopIfIdle(TimeSpan delay)
    {
        _handler.RemoveCallbacksAndMessages(StopToken);
        _handler.PostAtTime(() =>
        {
            BudsState.Refresh(); // re-evaluate live state (e.g. alternative-mode reconnect)
            var state = BudsState.Current;
            if (!state.IsConnected && !state.IsConnecting)
                StopService();
        }, StopToken, SystemClock.UptimeMillis() + (long)delay.TotalMilliseconds);
    }

    private void StopService()
    {
        _handler.RemoveCallbacksAndMessages(null);
        // Only stops if no newer start command is pending; destroying the service also removes its notification
        if (StopSelfResult(_lastStartId))
            _inForeground = false;
    }

    #region Notification

    private bool TryEnterForeground()
    {
        if (_inForeground)
            return true;
        if (OperatingSystem.IsAndroidVersionAtLeast(34) && !ForkIntents.HasBluetoothPermission(this))
        {
            // connectedDevice requires BLUETOOTH_CONNECT on API 34+
            Log.Warning("BudsConnectionService: Bluetooth permission missing");
            return false;
        }

        try
        {
            ServiceCompat.StartForeground(this, NotificationId, BuildNotification(BudsState.Current), TypeConnectedDevice);
            _inForeground = true;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "BudsConnectionService: cannot start in foreground");
            return false;
        }
    }

    /// <summary>
    /// Started with startForegroundService() but can't run: startForeground() must still be called before stopping,
    /// or the system kills the app. shortService has no permission prerequisites.
    /// </summary>
    private void BailOut()
    {
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(34))
                ServiceCompat.StartForeground(this, NotificationId, BuildNotification(BudsSnapshot.NotRunning), TypeShortService);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "BudsConnectionService: cannot enter foreground for bail-out");
        }
        // Widgets now route through the permission screen
        WidgetRenderer.UpdateAll(this);
        StopSelf();
    }

    private void CreateChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;
        var channel = new NotificationChannel(ChannelId, GetString(Resource.String.fork_notif_channel_name), NotificationImportance.Low)
        {
            Description = GetString(Resource.String.fork_notif_channel_desc)
        };
        channel.SetShowBadge(false);
        ((NotificationManager)GetSystemService(NotificationService)!).CreateNotificationChannel(channel);
    }

    private void UpdateNotification(BudsSnapshot state)
    {
        if (!_inForeground)
            return;
        NotificationManagerCompat.From(this)!.Notify(NotificationId, BuildNotification(state));
    }

    private Notification BuildNotification(BudsSnapshot state)
    {
        var builder = new NotificationCompat.Builder(this, ChannelId);
        builder.SetSmallIcon(Resource.Drawable.fork_ic_buds);
        builder.SetContentTitle(WidgetRenderer.Title(this, state));
        builder.SetContentText(WidgetRenderer.StatusLine(this, state));
        builder.SetContentIntent(ForkIntents.OpenAppPendingIntent(this));
        builder.SetOngoing(true);
        builder.SetOnlyAlertOnce(true);
        builder.SetShowWhen(false);
        builder.SetCategory(NotificationCompat.CategoryService);
        builder.SetForegroundServiceBehavior(NotificationCompat.ForegroundServiceImmediate);

        if (state.IsConnected || state.IsConnecting)
            builder.AddAction(0, GetString(Resource.String.fork_action_disconnect),
                ForkIntents.ActionPendingIntent(this, ForkIntents.ActionDisconnect, ForkIntents.RequestNotificationDisconnect));
        else if (state.CoreRunning && state.HasValidDevice)
            builder.AddAction(0, GetString(Resource.String.fork_action_connect),
                ForkIntents.ActionPendingIntent(this, ForkIntents.ActionConnect, ForkIntents.RequestNotificationConnect));

        return builder.Build()!;
    }

    #endregion

    #region Link loss

    // The Android backend doesn't reliably notice when the buds go away (its connection check is a no-op),
    // so listen for the ACL link of our device (or the adapter) going down and disconnect the core explicitly.
    private void RegisterLinkReceiver()
    {
        if (_linkReceiver != null)
            return;
        _linkReceiver = new LinkReceiver();
        var filter = new IntentFilter(BluetoothDevice.ActionAclDisconnected);
        filter.AddAction(BluetoothAdapter.ActionStateChanged);
        // Protected system broadcasts: exported is required to receive them on API < 33 and can't be spoofed
        ContextCompat.RegisterReceiver(this, _linkReceiver, filter, ContextCompat.ReceiverExported);
    }

    private class LinkReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            var bt = BluetoothImpl.Instance;
            if (intent?.Action == BluetoothAdapter.ActionStateChanged)
            {
                var adapterState = (State)intent.GetIntExtra(BluetoothAdapter.ExtraState, -1);
                if (adapterState is not (State.TurningOff or State.Off))
                    return;
                Log.Information("BudsConnectionService: Bluetooth turned off");
            }
            else if (intent?.Action == BluetoothDevice.ActionAclDisconnected)
            {
                var device = OperatingSystem.IsAndroidVersionAtLeast(33)
                    ? (BluetoothDevice?)intent.GetParcelableExtra(BluetoothDevice.ExtraDevice, Java.Lang.Class.FromType(typeof(BluetoothDevice)))
#pragma warning disable CA1422
                    : (BluetoothDevice?)intent.GetParcelableExtra(BluetoothDevice.ExtraDevice);
#pragma warning restore CA1422
                var current = bt.Device.Current?.MacAddress;
                if (device?.Address == null || current == null ||
                    !string.Equals(device.Address, current, StringComparison.OrdinalIgnoreCase))
                    return;
                Log.Information("BudsConnectionService: ACL link to {Mac} lost", current);
            }
            else
            {
                return;
            }

            if (bt.IsConnected)
                _ = bt.DisconnectAsync();
            else
                BudsState.SetConnecting(false);
        }
    }

    #endregion
}
