using System;
using System.Reflection;
using Android.Content;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls.ApplicationLifetimes;
using GalaxyBudsClient.Platform;
using ReactiveUI.Avalonia;
using Serilog;

namespace GalaxyBudsClient.Android.Fork;

/// <summary>
/// Brings the shared core and Avalonia up when the process was started by the service (e.g. from a widget)
/// instead of by <see cref="MainActivity"/>. See Fork/README.md for why Avalonia must be set up here and
/// why the lifetime is handed to <see cref="AvaloniaMainActivity"/> by reflection.
/// </summary>
/// <remarks>
/// Nothing may touch an Avalonia type (Application, controls, Dispatcher.UIThread, ...) before Avalonia's setup:
/// any AvaloniaObject construction reads Dispatcher.UIThread, which then caches a dead NullDispatcherImpl for the
/// whole process (white UI, no core events). That's why <see cref="IsInitialized"/> doesn't use Application.Current.
/// </remarks>
internal static class ForkAndroidBootstrap
{
    // AvaloniaMainActivity's private static SingleViewLifetime: set by MainActivity (or by us) right before Avalonia's setup
    private static readonly FieldInfo? LifetimeField =
        typeof(AvaloniaMainActivity).GetField("Lifetime", BindingFlags.NonPublic | BindingFlags.Static);

    private static bool _bootstrapped;

    /// <summary>Avalonia (and so the core) is set up or being set up in this process, by MainActivity or by us.</summary>
    public static bool IsInitialized => _bootstrapped || LifetimeField?.GetValue(null) != null;

    /// <summary>Must be called on the main thread. Returns true if this call performed the initialization.</summary>
    public static bool EnsureInitialized(Context context)
    {
        if (Looper.MyLooper() != Looper.MainLooper)
            throw new InvalidOperationException("ForkAndroidBootstrap must run on the main thread");
        if (IsInitialized)
            return false;

        // Resolve the Avalonia internals first, so nothing is initialized if they changed (Avalonia upgrade)
        if (LifetimeField == null)
            throw new MissingFieldException("AvaloniaMainActivity.Lifetime not found (Avalonia upgrade?)");
        var lifetimeType = typeof(AvaloniaMainActivity).Assembly.GetType("Avalonia.Android.SingleViewLifetime")
                           ?? throw new TypeLoadException("Avalonia.Android.SingleViewLifetime not found (Avalonia upgrade?)");
        var lifetime = (IApplicationLifetime)Activator.CreateInstance(lifetimeType, nonPublic: true)!;

        // Like AvaloniaMainActivity: publish the lifetime before setup. A MainActivity created later reuses it (and the
        // MainView) instead of setting Avalonia up a second time, which would throw and reallocate the connection.
        _bootstrapped = true;
        LifetimeField.SetValue(null, lifetime);
        try
        {
            AppBuilder.Configure<App>()
                .UseAndroid()
                .WithInterFont()
                .LogToTrace()
                .UseReactiveUI()
                // Mirror of MainActivity.CustomizeAppBuilder (re-check after upstream merges). Done once Avalonia's Android
                // threading is registered, so core code touching Dispatcher.UIThread gets the real dispatcher.
                .AfterPlatformServicesSetup(_ =>
                {
                    Program.Startup(false, new LogcatSink("GbClient"));
                    PlatformImpl.InjectExternalBackend(new ForkPlatformImplCreator(context.ApplicationContext ?? context));
                })
                // Runs before App.OnFrameworkInitializationCompleted starts its auto-connect, so no event is missed
                .AfterSetup(_ => BudsState.EnsureInitialized(context))
                .SetupWithLifetime(lifetime);
        }
        catch (Exception ex)
        {
            // Avalonia's setup is one-shot per process; a half-initialized process can't recover. Start clean next time.
            Log.Fatal(ex, "ForkAndroidBootstrap: Avalonia setup failed, killing process");
            global::Android.OS.Process.KillProcess(global::Android.OS.Process.MyPid());
            throw;
        }

        Log.Information("ForkAndroidBootstrap: core initialized without activity (dispatcher: {Impl})", UiDispatcherImpl());
        return true;
    }

    // Diagnostics only, after setup: which dispatcher implementation Avalonia picked (NullDispatcherImpl = broken)
    private static string UiDispatcherImpl()
    {
        var dispatcher = typeof(Avalonia.Threading.Dispatcher).GetField("s_uiThread", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        var impl = dispatcher == null ? null
            : typeof(Avalonia.Threading.Dispatcher).GetField("_impl", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(dispatcher);
        return impl?.GetType().Name ?? "<none>";
    }
}
