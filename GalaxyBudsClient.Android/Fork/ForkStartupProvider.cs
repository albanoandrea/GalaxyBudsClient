using System;
using Android.App;
using Android.Content;
using Android.Database;
using Android.OS;
using Serilog;

namespace GalaxyBudsClient.Android.Fork;

/// <summary>
/// Process-start hook without touching MainActivity: content providers are created before any activity.
/// Once MainActivity has set the core up, follow its connection state so that a connection made from the UI
/// also gets the foreground service (survives closing the app) and widgets stay current.
/// </summary>
[ContentProvider(
    ["com.andrea.galaxybudsclient.fork.startup"],
    Name = "com.andrea.galaxybudsclient.fork.ForkStartupProvider",
    Exported = false)]
internal class ForkStartupProvider : ContentProvider
{
    public override bool OnCreate()
    {
        // Before anything can set Avalonia up (MainActivity or ForkAndroidBootstrap): Dispatcher.UIThread is created lazily
        // and only then picks its backend. Binds into AvaloniaLocator only, doesn't touch the Dispatcher.
        ForkDispatcherImpl.Register();

        var appContext = Context?.ApplicationContext;
        if (appContext is global::Android.App.Application app)
        {
            // No activity yet (the process may have been started by the service); MainActivity's onStart flips it
            ForkConfig.IsUiVisible = false;
            app.RegisterActivityLifecycleCallbacks(new LifecycleCallbacks(appContext));
            // A new process means no connection: reset widgets left over from a previous process
            // (also rebuilds their PendingIntents for the current permission state). Doesn't touch the core.
            WidgetRenderer.UpdateAll(appContext);
        }
        return true;
    }

    public override ICursor? Query(global::Android.Net.Uri uri, string[]? projection, string? selection, string[]? selectionArgs, string? sortOrder) => null;
    public override string? GetType(global::Android.Net.Uri uri) => null;
    public override global::Android.Net.Uri? Insert(global::Android.Net.Uri uri, ContentValues? values) => null;
    public override int Delete(global::Android.Net.Uri uri, string? selection, string[]? selectionArgs) => 0;
    public override int Update(global::Android.Net.Uri uri, ContentValues? values, string? selection, string[]? selectionArgs) => 0;

    private class LifecycleCallbacks(Context appContext) : Java.Lang.Object, global::Android.App.Application.IActivityLifecycleCallbacks
    {
        private bool _linked;

        public void OnActivityResumed(Activity activity)
        {
            if (activity is not MainActivity || !ForkAndroidBootstrap.IsInitialized)
                return;

            BudsState.EnsureInitialized(appContext);
            if (!_linked)
            {
                _linked = true;
                Log.Information("ForkStartupProvider: UI dispatcher: {Impl}", ForkAndroidBootstrap.UiDispatcherImpl());
                BudsState.Changed += OnStateChanged;
            }
            // The process only counts as foreground (allowed to start the service) shortly after onResume
            new Handler(Looper.MainLooper!).PostDelayed(() => OnStateChanged(BudsState.Current), 1000);
        }

        private void OnStateChanged(BudsSnapshot state)
        {
            if (BudsConnectionService.IsRunning || !(state.IsConnected || state.IsConnecting))
                return;
            try
            {
                ForkIntents.StartService(appContext, ForkIntents.ActionAttach);
            }
            catch (Exception ex)
            {
                // Not allowed while the app is in the background (ForegroundServiceStartNotAllowedException)
                Log.Warning("ForkStartupProvider: cannot start connection service: {Error}", ex.Message);
            }
        }

        public void OnActivityCreated(Activity activity, Bundle? savedInstanceState) { }

        // Stops the UI's periodic requests to the buds while the app is not on screen (see ForkConfig.IsUiVisible)
        public void OnActivityStarted(Activity activity)
        {
            if (activity is MainActivity)
                ForkConfig.IsUiVisible = true;
        }

        public void OnActivityPaused(Activity activity) { }

        public void OnActivityStopped(Activity activity)
        {
            if (activity is MainActivity)
                ForkConfig.IsUiVisible = false;
        }
        public void OnActivitySaveInstanceState(Activity activity, Bundle outState) { }
        public void OnActivityDestroyed(Activity activity) { }
    }
}
