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
}
