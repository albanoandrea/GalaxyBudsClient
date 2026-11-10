namespace GalaxyBudsClient;

/// <summary>
/// Fork-specific feature switches (see CLAUDE.md).
/// Upstream code is gated through these flags instead of being deleted, to keep merges from upstream conflict-free.
/// </summary>
public static class ForkConfig
{
    /// <summary>Firmware flashing/downgrading from the app</summary>
    public static readonly bool FirmwareUpdatesEnabled = false;

    /// <summary>Any outbound network service: crash reporting (Sentry), crowdsourced experiments, firmware download server</summary>
    public static readonly bool OnlineServicesEnabled = false;

    /// <summary>
    /// Open the RFCOMM link to the last device as soon as the app starts.
    /// Off on desktop so that launching the app doesn't pull the buds away from the phone; connect manually instead.
    /// Kept on Android, where the phone is the device the buds should stay with.
    /// </summary>
    public static readonly bool ConnectOnStartup = System.OperatingSystem.IsAndroid();

    /// <summary>
    /// BluetoothImpl's data consumer sleeps until a frame arrives (then still batches for 50 ms) instead of polling
    /// every 50 ms forever. Off: upstream polling (see BluetoothImpl.Fork.cs).
    /// </summary>
    public static readonly bool EventDrivenDataConsumer = true;

    /// <summary>
    /// Runtime state, not a switch: false while no window of the app is on screen. Only Android maintains it
    /// (ForkStartupProvider); there the foreground service keeps the process, and so the UI's view models, alive in the
    /// background. Periodic UI refreshes that talk to the buds (HomePageViewModel's 12 s poll) are skipped meanwhile.
    /// </summary>
    public static volatile bool IsUiVisible = true;
}
