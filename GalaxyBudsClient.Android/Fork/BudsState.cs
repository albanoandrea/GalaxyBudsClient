using System;
using Android.Content;
using Android.OS;
using GalaxyBudsClient.Message;
using GalaxyBudsClient.Message.Decoder;
using GalaxyBudsClient.Model.Specifications;
using GalaxyBudsClient.Platform;
using Serilog;

namespace GalaxyBudsClient.Android.Fork;

/// <summary>Immutable view of what widgets, the notification and tiles display.</summary>
internal sealed record BudsSnapshot(
    bool CoreRunning,
    bool HasValidDevice,
    bool IsConnected,
    bool IsConnecting,
    string? DeviceName,
    int? BatteryL,
    int? BatteryR,
    int? BatteryCase)
{
    public static readonly BudsSnapshot NotRunning = new(false, false, false, false, null, null, null, null);
}

/// <summary>
/// Tracks connection + battery state of the shared core and raises <see cref="Changed"/> on the main thread.
/// Only touches the core after it is initialized (activity or <see cref="ForkAndroidBootstrap"/>).
/// </summary>
internal static class BudsState
{
    // Longer than Android's RFCOMM connect timeout, so only attempts that never report back are cut off
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
    private static readonly Java.Lang.String ConnectTimeoutToken = new("connect-timeout");

    private static readonly Handler MainHandler = new(Looper.MainLooper!);
    private static BluetoothImpl? _subscribed;
    private static volatile bool _connecting;
    private static Context? _appContext;

    public static BudsSnapshot Current { get; private set; } = BudsSnapshot.NotRunning;

    /// <summary>Raised on the main thread whenever <see cref="Current"/> may have changed.</summary>
    public static event Action<BudsSnapshot>? Changed;

    /// <summary>
    /// Call on the main thread once Avalonia's Application exists. Safe to call repeatedly.
    /// The first call assumes the App's start-up auto-connect is running (if a device is set up),
    /// because that attempt may have announced itself before we subscribed.
    /// </summary>
    public static void EnsureInitialized(Context context)
    {
        _appContext ??= context.ApplicationContext ?? context;
        if (!ForkAndroidBootstrap.IsInitialized)
            return;

        var bt = BluetoothImpl.Instance;
        if (_subscribed != bt)
        {
            var first = _subscribed == null;
            if (first)
            {
                DeviceMessageCache.Init();
                SppMessageReceiver.Instance.BaseUpdate += OnBaseUpdate;
            }
            else
            {
                // BluetoothImpl.Reallocate() replaces the instance; follow it
                Subscribe(_subscribed!, false);
            }

            Subscribe(bt, true);
            _subscribed = bt;
            Log.Debug("BudsState: subscribed to BluetoothImpl");

            if (first && BluetoothImpl.HasValidDevice && !bt.IsConnected && string.IsNullOrEmpty(bt.LastErrorMessage))
                SetConnecting(true);
        }

        Refresh();
    }

    private static void Subscribe(BluetoothImpl bt, bool subscribe)
    {
        if (subscribe)
        {
            bt.Connected += OnConnected;
            bt.Connecting += OnConnecting;
            bt.Disconnected += OnDisconnected;
            bt.BluetoothError += OnBluetoothError;
            // MultipointPatcher briefly reconnects in "alternative" mode; keep showing that as connecting
            bt.ConnectedAlternative += OnAlternativeEvent;
            bt.ConnectingAlternative += OnAlternativeEvent;
            bt.DisconnectedAlternative += OnAlternativeEvent;
            bt.BluetoothErrorAlternative += OnAlternativeEvent;
        }
        else
        {
            bt.Connected -= OnConnected;
            bt.Connecting -= OnConnecting;
            bt.Disconnected -= OnDisconnected;
            bt.BluetoothError -= OnBluetoothError;
            bt.ConnectedAlternative -= OnAlternativeEvent;
            bt.ConnectingAlternative -= OnAlternativeEvent;
            bt.DisconnectedAlternative -= OnAlternativeEvent;
            bt.BluetoothErrorAlternative -= OnAlternativeEvent;
        }
    }

    /// <summary>Marks a connection attempt in progress (or over). Any thread.</summary>
    public static void SetConnecting(bool connecting)
    {
        _connecting = connecting;
        MainHandler.Post(() =>
        {
            MainHandler.RemoveCallbacksAndMessages(ConnectTimeoutToken);
            if (_connecting)
                MainHandler.PostAtTime(OnConnectTimeout, ConnectTimeoutToken,
                    SystemClock.UptimeMillis() + (long)ConnectTimeout.TotalMilliseconds);
            Refresh();
        });
    }

    private static void OnConnectTimeout()
    {
        if (!_connecting || _subscribed?.IsConnected == true)
            return;
        Log.Warning("BudsState: connection attempt timed out");
        SetConnecting(false);
    }

    /// <summary>Rebuilds <see cref="Current"/> and notifies listeners. Any thread (runs on the main thread).</summary>
    public static void Refresh()
    {
        if (Looper.MyLooper() != Looper.MainLooper)
        {
            MainHandler.Post(Refresh);
            return;
        }

        var previous = Current;
        Current = BuildSnapshot();
        if (Current != previous)
            Log.Debug("BudsState: {State}", Current);
        try
        {
            Changed?.Invoke(Current);
            if (_appContext != null)
                WidgetRenderer.UpdateAll(_appContext);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "BudsState: error while publishing state");
        }
    }

    private static BudsSnapshot BuildSnapshot()
    {
        if (_subscribed == null)
            return BudsSnapshot.NotRunning;

        var bt = BluetoothImpl.Instance;
        var connected = bt.IsConnected;
        var alternativeBusy = bt.AlternativeModeEnabled || bt.IsConnectedAlternative;

        int? left = null, right = null, batteryCase = null;
        var bsu = DeviceMessageCache.Instance.BasicStatusUpdate;
        if (connected && bsu != null)
        {
            // Same rules as TrayManager.RebuildBatteryInfo
            left = bsu.BatteryL > 0 ? bsu.BatteryL : null;
            right = bsu.BatteryR > 0 ? bsu.BatteryR : null;

            var c = bsu.BatteryCase;
            if (c > 100) // 101 = case disconnected
                c = DeviceMessageCache.Instance.BasicStatusUpdateWithValidCase?.BatteryCase ?? c;
            if (c is > 0 and <= 100 && bt.DeviceSpec.Supports(Features.CaseBattery))
                batteryCase = c;
        }

        return new BudsSnapshot(
            CoreRunning: true,
            HasValidDevice: BluetoothImpl.HasValidDevice,
            IsConnected: connected,
            IsConnecting: !connected && (_connecting || alternativeBusy),
            DeviceName: bt.Device.Current?.Name ?? bt.DeviceName,
            BatteryL: left,
            BatteryR: right,
            BatteryCase: batteryCase);
    }

    private static void OnConnected(object? sender, EventArgs e) => SetConnecting(false);
    private static void OnConnecting(object? sender, EventArgs e) => SetConnecting(true);
    private static void OnDisconnected(object? sender, string reason) => SetConnecting(false);
    private static void OnBluetoothError(object? sender, BluetoothException e) => SetConnecting(false);
    private static void OnAlternativeEvent(object? sender, object? e) => Refresh();
    // Posted: DeviceMessageCache stores the message in a handler of the same dispatch, which may run after ours
    private static void OnBaseUpdate(object? sender, IBasicStatusUpdate e) => MainHandler.Post(Refresh);
}
