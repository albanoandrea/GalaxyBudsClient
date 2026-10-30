namespace GalaxyBudsClient.Platform;

/// <summary>
/// Fork-specific switches for the platform backends, which can't see GalaxyBudsClient.ForkConfig (see CLAUDE.md).
/// </summary>
public static class ForkPlatformConfig
{
    /// <summary>
    /// Desktop backends re-open the control channel on their own whenever the OS reports the buds as connected/in range again.
    /// Off: the buds also talk to the phone, and the PC grabbing the channel kicks the phone off. Connect manually instead.
    /// </summary>
    public static readonly bool AutoReconnectEnabled = false;
}
