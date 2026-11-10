using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace GalaxyBudsClient.Android.Fork;

/// <summary>
/// Native (no Avalonia) settings screen, also the widget's configuration activity (long-press the widget on
/// Android 12+ to reach it; <c>configuration_optional</c> means adding the widget doesn't open it).
/// Must only use <see cref="ForkIntents"/> and Android APIs: it can run in a process where Avalonia isn't set up.
/// </summary>
[Activity(
    Name = "com.andrea.galaxybudsclient.fork.ForkSettingsActivity",
    Label = "@string/fork_widget_controls_label",
    Theme = "@style/fork_SettingsTheme",
    Exported = true)]
[IntentFilter(new[] { AppWidgetManager.ActionAppwidgetConfigure })]
internal class ForkSettingsActivity : Activity
{
    private Switch? _autoConnect;
    private bool _binding;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Configure contract: always answer OK with the widget id, so the launcher never drops the widget
        // (also when the user leaves with Back). Without an id (started from elsewhere) there is nothing to answer.
        var widgetId = Intent?.GetIntExtra(AppWidgetManager.ExtraAppwidgetId, AppWidgetManager.InvalidAppwidgetId)
                       ?? AppWidgetManager.InvalidAppwidgetId;
        if (widgetId != AppWidgetManager.InvalidAppwidgetId)
        {
            var result = new Intent().PutExtra(AppWidgetManager.ExtraAppwidgetId, widgetId);
            SetResult(Result.Ok, result);
        }

        SetContentView(Resource.Layout.fork_settings);

        // Edge-to-edge is enforced on recent Android versions: keep content out of the system bars
        var root = FindViewById<View>(Resource.Id.fork_settings_root);
        if (root != null)
            root.SetOnApplyWindowInsetsListener(
                new InsetsListener(root.PaddingLeft, root.PaddingTop, root.PaddingRight, root.PaddingBottom));

        _autoConnect = FindViewById<Switch>(Resource.Id.fork_settings_auto_connect);
        if (_autoConnect != null)
        {
            _autoConnect.CheckedChange += (_, e) =>
            {
                if (!_binding)
                    ForkIntents.SetAutoConnectOnAttach(this, e.IsChecked);
            };
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (_autoConnect == null)
            return;

        _binding = true;
        _autoConnect.Checked = ForkIntents.AutoConnectOnAttach(this);
        _binding = false;
    }

    private sealed class InsetsListener(int l, int t, int r, int b) : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View v, WindowInsets insets)
        {
            v.SetPadding(l + insets.SystemWindowInsetLeft, t + insets.SystemWindowInsetTop,
                r + insets.SystemWindowInsetRight, b + insets.SystemWindowInsetBottom);
            return insets;
        }
    }
}
