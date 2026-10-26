using Android.App;
using Android.Appwidget;
using Android.Content;

namespace GalaxyBudsClient.Android.Fork;

/// <summary>Home screen widget: device name, connection/battery line and a connect/disconnect button.</summary>
[BroadcastReceiver(
    Name = "com.andrea.galaxybudsclient.fork.ConnectWidgetProvider",
    Exported = true,
    Label = "@string/fork_widget_connect_label")]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/fork_widget_connect_info")]
internal class ConnectWidgetProvider : AppWidgetProvider
{
    // System-triggered updates (add, resize, boot) only redraw; they must not start the service
    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        if (context == null || appWidgetManager == null || appWidgetIds == null)
            return;
        WidgetRenderer.UpdateConnectWidgets(context, appWidgetManager, appWidgetIds);
    }
}
