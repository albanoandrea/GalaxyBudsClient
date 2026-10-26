using System.Collections.Generic;
using Android.Appwidget;
using Android.Content;
using Android.Widget;
using Serilog;

namespace GalaxyBudsClient.Android.Fork;

/// <summary>Builds the RemoteViews of all fork widgets from <see cref="BudsState.Current"/>.</summary>
internal static class WidgetRenderer
{
    public static void UpdateAll(Context context)
    {
        try
        {
            var manager = AppWidgetManager.GetInstance(context)!;
            var ids = manager.GetAppWidgetIds(new ComponentName(context, Java.Lang.Class.FromType(typeof(ConnectWidgetProvider))));
            if (ids is { Length: > 0 })
                UpdateConnectWidgets(context, manager, ids);
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "WidgetRenderer: update failed");
        }
    }

    public static void UpdateConnectWidgets(Context context, AppWidgetManager manager, int[] ids)
    {
        var state = BudsState.Current;
        var views = new RemoteViews(context.PackageName, Resource.Layout.fork_widget_connect);

        views.SetTextViewText(Resource.Id.fork_widget_title, Title(context, state));
        views.SetTextViewText(Resource.Id.fork_widget_status, StatusLine(context, state));
        // The button sends the action its label shows, so a stale widget (process died) never does the opposite
        if (state.CoreRunning && !state.HasValidDevice)
        {
            views.SetTextViewText(Resource.Id.fork_widget_button, context.GetString(Resource.String.fork_action_open_app));
            views.SetOnClickPendingIntent(Resource.Id.fork_widget_button, ForkIntents.OpenAppPendingIntent(context));
        }
        else if (state.IsConnected || state.IsConnecting)
        {
            views.SetTextViewText(Resource.Id.fork_widget_button, context.GetString(Resource.String.fork_action_disconnect));
            views.SetOnClickPendingIntent(Resource.Id.fork_widget_button,
                ForkIntents.ActionPendingIntent(context, ForkIntents.ActionDisconnect, ForkIntents.RequestWidgetDisconnect));
        }
        else
        {
            views.SetTextViewText(Resource.Id.fork_widget_button, context.GetString(Resource.String.fork_action_connect));
            views.SetOnClickPendingIntent(Resource.Id.fork_widget_button,
                ForkIntents.ActionPendingIntent(context, ForkIntents.ActionConnect, ForkIntents.RequestWidgetConnect));
        }
        views.SetOnClickPendingIntent(Resource.Id.fork_widget_root, ForkIntents.OpenAppPendingIntent(context));

        manager.UpdateAppWidget(ids, views);
    }

    public static string Title(Context context, BudsSnapshot state) =>
        string.IsNullOrWhiteSpace(state.DeviceName) ? context.GetString(Resource.String.fork_default_device_name) : state.DeviceName;

    public static string StatusLine(Context context, BudsSnapshot state)
    {
        if (state.CoreRunning && !state.HasValidDevice)
            return context.GetString(Resource.String.fork_status_no_device);
        if (state.IsConnecting)
            return context.GetString(Resource.String.fork_status_connecting);
        if (!state.IsConnected)
            return context.GetString(Resource.String.fork_status_disconnected);

        var parts = new List<string>();
        if (state.BatteryL is { } l)
            parts.Add(context.GetString(Resource.String.fork_battery_left, l));
        if (state.BatteryR is { } r)
            parts.Add(context.GetString(Resource.String.fork_battery_right, r));
        if (state.BatteryCase is { } c)
            parts.Add(context.GetString(Resource.String.fork_battery_case, c));
        return parts.Count == 0
            ? context.GetString(Resource.String.fork_status_connected)
            : string.Join(" · ", parts);
    }
}
