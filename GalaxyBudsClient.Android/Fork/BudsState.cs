using System;
using Android.Content;
using Android.OS;
using GalaxyBudsClient.Message;
using GalaxyBudsClient.Message.Decoder;
using GalaxyBudsClient.Message.Parameter;
using GalaxyBudsClient.Model.Constants;
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
    int? BatteryCase,
    NoiseControlModes? NoiseMode = null,
    bool HasAmbient = false,
    bool HasAdaptive = false,
    bool HasAnc = false)
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
    private static NoiseControlModes? _noiseMode;

    /// <summary>
    /// Noise control modes of a model (besides Off). Reads the spec rules directly instead of IDeviceSpec.Supports,
    /// which touches an Avalonia view: this also runs before the core is set up (see Fork/README.md pitfalls).
    /// </summary>
    public static (bool Ambient, bool Adaptive, bool Anc) ModeSupportFor(Models model)
    {
        var rules = DeviceSpecHelper.FindByModel(model)?.Rules;
        if (rules == null)
            return (false, false, false);
        var noiseControl = rules.ContainsKey(Features.NoiseControl);
        return (
            noiseControl || rules.ContainsKey(Features.AmbientSound),
            // Adaptive (NOISE_CONTROLS = 3) exists on these models, but no upstream spec declares
            // Features.NoiseControlAdaptive (enabling it there would also change how the ESU touch options are parsed)
            noiseControl && (rules.ContainsKey(Features.NoiseControlAdaptive) || model is Models.Buds3Pro or Models.Buds4Pro),
            rules.ContainsKey(Features.Anc));
    }

    /// <summary>
    /// Device configured in the app (the last used one), read from settings.json without loading the core's Settings
    /// (its static constructor must not run before Avalonia is set up). <see cref="Models.NULL"/> if unknown.
    /// </summary>
    public static (Models Model, string? Name) ConfiguredDevice()
    {
        var path = PlatformUtils.CombineDataPath("settings.json");
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path),
                new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true });
            var root = doc.RootElement;
            var lastMac = Property(root, "lastDeviceMac")?.ToString();
            if (Property(root, "devices") is not { ValueKind: System.Text.Json.JsonValueKind.Array } devices)
            {
                var keys = new System.Collections.Generic.List<string>();
                foreach (var p in root.EnumerateObject())
                    keys.Add(p.Name);
                global::Android.Util.Log.Warn("GbFork", $"BudsState: no devices in {path}; keys: {string.Join(",", keys)}");
                return (Models.NULL, null);
            }

            (Models Model, string? Name) result = (Models.NULL, null);
            foreach (var device in devices.EnumerateArray())
            {
                if (Property(device, "model") is not { } value ||
                    !Enum.TryParse<Models>(value.ToString(), true, out var parsed) || parsed == Models.NULL)
                    continue;
                if (result.Model == Models.NULL || Property(device, "macAddress")?.ToString() == lastMac)
                    result = (parsed, Property(device, "name")?.ToString());
            }
            if (result.Model == Models.NULL)
                global::Android.Util.Log.Warn("GbFork", $"BudsState: no device model in {path}");
            return result;
        }
        catch (Exception ex)
        {
            // Serilog is not set up yet when this runs, so log to logcat directly
            global::Android.Util.Log.Warn("GbFork", $"BudsState: could not read the configured device from {path}: {ex}");
            return (Models.NULL, null);
        }
    }

    // The serializer reads case-insensitively, so match that
    private static System.Text.Json.JsonElement? Property(System.Text.Json.JsonElement element, string name)
    {
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object)
            return null;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }
        return null;
    }

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
                SubscribeNoiseControl();
                if (DeviceMessageCache.Instance.ExtendedStatusUpdate is { } esu)
                    _noiseMode = NoiseModeFrom(esu);
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

        var modes = ModeSupportFor(bt.CurrentModel);
        return new BudsSnapshot(
            CoreRunning: true,
            HasValidDevice: BluetoothImpl.HasValidDevice,
            IsConnected: connected,
            IsConnecting: !connected && (_connecting || alternativeBusy),
            DeviceName: bt.Device.Current?.Name ?? bt.DeviceName,
            BatteryL: left,
            BatteryR: right,
            BatteryCase: batteryCase,
            NoiseMode: connected ? _noiseMode : null,
            HasAmbient: modes.Ambient,
            HasAdaptive: modes.Adaptive,
            HasAnc: modes.Anc);
    }

    /// <summary>
    /// Sets the noise control mode the same way NoiseControlPageViewModel does. The app UI follows
    /// through the NOISE_CONTROLS acknowledgement. Main thread.
    /// </summary>
    public static async void SetNoiseMode(NoiseControlModes mode)
    {
        var bt = BluetoothImpl.Instance;
        if (!bt.IsConnected)
            return;

        // Optimistic: the widget reacts at once; the ack or the next status update corrects it if needed
        _noiseMode = mode;
        Refresh();
        try
        {
            if (bt.DeviceSpec.Supports(Features.NoiseControl))
            {
                await bt.SendRequestAsync(MsgIds.NOISE_CONTROLS, (byte)mode);
            }
            else
            {
                if (bt.DeviceSpec.Supports(Features.AmbientSound))
                    await bt.SendRequestAsync(MsgIds.SET_AMBIENT_MODE, mode == NoiseControlModes.AmbientSound);
                if (bt.DeviceSpec.Supports(Features.Anc))
                    await bt.SendRequestAsync(MsgIds.SET_NOISE_REDUCTION, mode == NoiseControlModes.NoiseReduction);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "BudsState: failed to set noise control mode {Mode}", mode);
        }
    }

    private static NoiseControlModes NoiseModeFrom(ExtendedStatusUpdateDecoder e)
    {
        if (BluetoothImpl.Instance.DeviceSpec.Supports(Features.NoiseControl))
            return e.NoiseControlMode;
        return e.AmbientSoundEnabled ? NoiseControlModes.AmbientSound
            : e.NoiseCancelling ? NoiseControlModes.NoiseReduction
            : NoiseControlModes.Off;
    }

    // Same sources as NoiseControlPageViewModel: status update, buds-side changes (touch, Samsung app) and acks
    private static void SubscribeNoiseControl()
    {
        var receiver = SppMessageReceiver.Instance;
        receiver.ExtendedStatusUpdate += (_, e) => SetNoiseModeFromBuds(NoiseModeFrom(e));
        receiver.NoiseControlUpdateResponse += (_, mode) => SetNoiseModeFromBuds(mode);
        receiver.AncEnabledUpdateResponse += (_, enabled) => SetNoiseModeFromBuds(
            enabled ? NoiseControlModes.NoiseReduction
            : _noiseMode == NoiseControlModes.NoiseReduction ? NoiseControlModes.Off : _noiseMode);
        receiver.AmbientEnabledUpdateResponse += (_, enabled) => SetNoiseModeFromBuds(
            enabled ? NoiseControlModes.AmbientSound
            : _noiseMode == NoiseControlModes.AmbientSound ? NoiseControlModes.Off : _noiseMode);
        receiver.AcknowledgementResponse += (_, ack) =>
        {
            if (ack.Id == MsgIds.NOISE_CONTROLS && ack.Parameters is SimpleAckParameter param)
                SetNoiseModeFromBuds((NoiseControlModes)param.Value);
        };
    }

    private static void SetNoiseModeFromBuds(NoiseControlModes? mode)
    {
        _noiseMode = mode;
        Refresh();
    }

    private static void OnConnected(object? sender, EventArgs e) => SetConnecting(false);
    private static void OnConnecting(object? sender, EventArgs e) => SetConnecting(true);
    private static void OnDisconnected(object? sender, string reason) => SetConnecting(false);
    private static void OnBluetoothError(object? sender, BluetoothException e) => SetConnecting(false);
    private static void OnAlternativeEvent(object? sender, object? e) => Refresh();
    // Posted: DeviceMessageCache stores the message in a handler of the same dispatch, which may run after ours
    private static void OnBaseUpdate(object? sender, IBasicStatusUpdate e) => MainHandler.Post(Refresh);
}
