using Android.App;
using Android.Appwidget;
using Android.Content;

namespace GalaxyBudsClient.Android.Fork;

/// <summary>Home screen widget: connect/disconnect, L / R / case battery and noise control mode.</summary>
[BroadcastReceiver(
    Name = "com.andrea.galaxybudsclient.fork.ControlsWidgetProvider",
    Exported = true,
    Label = "@string/fork_widget_controls_label")]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/fork_widget_controls_info")]
internal class ControlsWidgetProvider : AppWidgetProvider
{
    // System-triggered updates (add, resize, boot) only redraw; they must not start the service
    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        if (context == null || appWidgetManager == null || appWidgetIds == null)
            return;
        WidgetRenderer.UpdateControlsWidgets(context, appWidgetManager, appWidgetIds);
    }

    // Resized: before Android 12 the layout is picked from the reported size, so redraw
    public override void OnAppWidgetOptionsChanged(Context? context, AppWidgetManager? appWidgetManager, int appWidgetId,
        global::Android.OS.Bundle? newOptions)
    {
        if (context == null || appWidgetManager == null)
            return;
        WidgetRenderer.UpdateControlsWidgets(context, appWidgetManager, [appWidgetId]);
    }
}
