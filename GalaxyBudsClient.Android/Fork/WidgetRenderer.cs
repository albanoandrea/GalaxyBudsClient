using System;
using System.Collections.Generic;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Graphics;
using Android.Text;
using Android.Text.Style;
using Android.Views;
using SizeF = Android.Util.SizeF;
using Android.Widget;
using GalaxyBudsClient.Model.Constants;
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
            var ids = manager.GetAppWidgetIds(new ComponentName(context, Java.Lang.Class.FromType(typeof(ControlsWidgetProvider))));
            if (ids is { Length: > 0 })
                UpdateControlsWidgets(context, manager, ids);
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "WidgetRenderer: update failed");
        }
    }

    private static readonly (NoiseControlModes Mode, int Cell, int Icon, int Label, int RequestCode)[] ControlModes =
    [
        (NoiseControlModes.Off, Resource.Id.fork_controls_mode_off, Resource.Id.fork_controls_mode_off_icon,
            Resource.Id.fork_controls_mode_off_label, ForkIntents.RequestWidgetModeOff),
        (NoiseControlModes.AmbientSound, Resource.Id.fork_controls_mode_ambient, Resource.Id.fork_controls_mode_ambient_icon,
            Resource.Id.fork_controls_mode_ambient_label, ForkIntents.RequestWidgetModeAmbient),
        (NoiseControlModes.Adaptive, Resource.Id.fork_controls_mode_adaptive, Resource.Id.fork_controls_mode_adaptive_icon,
            Resource.Id.fork_controls_mode_adaptive_label, ForkIntents.RequestWidgetModeAdaptive),
        (NoiseControlModes.NoiseReduction, Resource.Id.fork_controls_mode_anc, Resource.Id.fork_controls_mode_anc_icon,
            Resource.Id.fork_controls_mode_anc_label, ForkIntents.RequestWidgetModeAnc)
    ];

    private enum ControlsSize { Compact3, Compact4, Full }

    // Thresholds in dp. Pixel launcher: 3 cells ~ 280 wide, 4 cells ~ 370, 1 row ~ 90 high, 2 rows ~ 200
    private const float CompactMinWidth = 200f;
    private const float CompactMinHeight = 40f;
    private const float Compact4MinWidth = 330f;
    private const float FullMinWidth = 250f;
    private const float FullMinHeight = 150f;

    public static void UpdateControlsWidgets(Context context, AppWidgetManager manager, int[] ids)
    {
        var state = BudsState.Current;
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            // The launcher picks the layout with the largest area that fits, also while resizing
            var sizes = new Dictionary<SizeF, RemoteViews>
            {
                [new SizeF(CompactMinWidth, CompactMinHeight)] = BuildControls(context, state, ControlsSize.Compact3),
                [new SizeF(Compact4MinWidth, CompactMinHeight)] = BuildControls(context, state, ControlsSize.Compact4),
                [new SizeF(FullMinWidth, FullMinHeight)] = BuildControls(context, state, ControlsSize.Full)
            };
            manager.UpdateAppWidget(ids, new RemoteViews(sizes));
            return;
        }

        foreach (var id in ids)
        {
            var options = manager.GetAppWidgetOptions(id);
            var width = options?.GetInt(AppWidgetManager.OptionAppwidgetMinWidth) ?? 0;
            var height = options?.GetInt(AppWidgetManager.OptionAppwidgetMaxHeight) ?? 0;
            var size = height >= FullMinHeight ? ControlsSize.Full
                : width >= Compact4MinWidth ? ControlsSize.Compact4
                : ControlsSize.Compact3;
            manager.UpdateAppWidget(id, BuildControls(context, state, size));
        }
    }

    private static RemoteViews BuildControls(Context context, BudsSnapshot state, ControlsSize size)
    {
        var full = size == ControlsSize.Full;
        var views = new RemoteViews(context.PackageName,
            full ? Resource.Layout.fork_widget_controls : Resource.Layout.fork_widget_controls_compact);

        // Compact: one tiny line with the battery (or the connection state); full: name + state, battery bars
        if (full)
            views.SetTextViewText(Resource.Id.fork_controls_title, Title(context, state));
        views.SetTextViewText(Resource.Id.fork_controls_status, full
            ? context.GetString(
                state.CoreRunning && !state.HasValidDevice ? Resource.String.fork_status_no_device :
                state.IsConnecting ? Resource.String.fork_status_connecting :
                state.IsConnected ? Resource.String.fork_status_connected :
                Resource.String.fork_status_disconnected)
            : StatusLine(context, state));

        // Same rule as the connect widget: the button sends the action it shows
        PendingIntent buttonIntent;
        int buttonText, buttonBackground, buttonColor, buttonIcon;
        if (state.CoreRunning && !state.HasValidDevice)
        {
            (buttonText, buttonBackground, buttonColor, buttonIcon) = (Resource.String.fork_action_open_app,
                Resource.Drawable.fork_widget_button_background, Resource.Color.fork_widget_button_text,
                Resource.Drawable.fork_ic_bluetooth_off);
            buttonIntent = ForkIntents.OpenAppPendingIntent(context);
        }
        else if (state.IsConnected || state.IsConnecting)
        {
            (buttonText, buttonBackground, buttonColor, buttonIcon) = (Resource.String.fork_action_disconnect,
                Resource.Drawable.fork_widget_button_tonal_background, Resource.Color.fork_widget_text,
                Resource.Drawable.fork_ic_bluetooth);
            buttonIntent = ForkIntents.ActionPendingIntent(context, ForkIntents.ActionDisconnect, ForkIntents.RequestWidgetDisconnect);
        }
        else
        {
            (buttonText, buttonBackground, buttonColor, buttonIcon) = (Resource.String.fork_action_connect,
                Resource.Drawable.fork_widget_button_background, Resource.Color.fork_widget_button_text,
                Resource.Drawable.fork_ic_bluetooth_off);
            buttonIntent = ForkIntents.ActionPendingIntent(context, ForkIntents.ActionConnect, ForkIntents.RequestWidgetConnect);
        }

        if (full)
        {
            views.SetTextViewText(Resource.Id.fork_controls_button, context.GetString(buttonText));
            views.SetInt(Resource.Id.fork_controls_button, "setBackgroundResource", buttonBackground);
            SetTextColor(views, context, Resource.Id.fork_controls_button, buttonColor);
        }
        else
        {
            // Icon toggle: filled when connected, accent icon while connecting, grey when disconnected
            views.SetImageViewResource(Resource.Id.fork_controls_button, buttonIcon);
            views.SetInt(Resource.Id.fork_controls_button, "setBackgroundResource", state.IsConnected
                ? Resource.Drawable.fork_widget_mode_circle_selected
                : Resource.Drawable.fork_widget_mode_circle);
            SetImageTint(views, context, Resource.Id.fork_controls_button,
                state.IsConnected ? Resource.Color.fork_widget_on_accent :
                state.IsConnecting ? Resource.Color.fork_widget_accent :
                Resource.Color.fork_widget_mode_icon);
            views.SetContentDescription(Resource.Id.fork_controls_button, context.GetString(buttonText));
        }
        views.SetOnClickPendingIntent(Resource.Id.fork_controls_button, buttonIntent);

        if (full)
        {
            // Battery bars stay in place while disconnected, so the widget does not jump around
            SetBattery(views, context, Resource.Id.fork_controls_battery_l_bar, Resource.Id.fork_controls_battery_l_text,
                state.IsConnected ? state.BatteryL : null);
            SetBattery(views, context, Resource.Id.fork_controls_battery_r_bar, Resource.Id.fork_controls_battery_r_text,
                state.IsConnected ? state.BatteryR : null);
            SetBattery(views, context, Resource.Id.fork_controls_battery_c_bar, Resource.Id.fork_controls_battery_c_text,
                state.IsConnected ? state.BatteryCase : null);
        }

        // Noise control: selected mode is a filled accent circle; everything is dimmed while not connected.
        // Only the modes the model supports; Adaptive also needs the 4-column width.
        var (hasAmbient, hasAdaptive, hasAnc) = ModeSupport(state);
        var shown = new Dictionary<NoiseControlModes, bool>
        {
            [NoiseControlModes.Off] = true,
            [NoiseControlModes.AmbientSound] = hasAmbient,
            [NoiseControlModes.Adaptive] = hasAdaptive && size != ControlsSize.Compact3,
            [NoiseControlModes.NoiseReduction] = hasAnc
        };
        if (full)
        {
            var columns = 0;
            foreach (var visible in shown.Values)
                columns += visible ? 1 : 0;
            views.SetViewVisibility(Resource.Id.fork_controls_track_4, columns == 4 ? ViewStates.Visible : ViewStates.Gone);
            views.SetViewVisibility(Resource.Id.fork_controls_track_3, columns == 3 ? ViewStates.Visible : ViewStates.Gone);
            views.SetViewVisibility(Resource.Id.fork_controls_track_2, columns == 2 ? ViewStates.Visible : ViewStates.Gone);
        }
        views.SetFloat(Resource.Id.fork_controls_modes, "setAlpha", state.IsConnected ? 1f : 0.45f);
        foreach (var (mode, cell, icon, label, requestCode) in ControlModes)
        {
            views.SetViewVisibility(cell, shown[mode] ? ViewStates.Visible : ViewStates.Gone);
            var selected = state.IsConnected && state.NoiseMode == mode;
            views.SetInt(icon, "setBackgroundResource", selected
                ? Resource.Drawable.fork_widget_mode_circle_selected
                : Resource.Drawable.fork_widget_mode_circle);
            SetImageTint(views, context, icon, selected ? Resource.Color.fork_widget_on_accent : Resource.Color.fork_widget_mode_icon);

            if (full)
            {
                SetTextColor(views, context, label, selected ? Resource.Color.fork_widget_accent : Resource.Color.fork_widget_text);
                var text = new SpannableString(context.GetString(ModeLabel(mode)));
                if (selected)
                    text.SetSpan(new StyleSpan(TypefaceStyle.Bold), 0, text.Length(), SpanTypes.ExclusiveExclusive);
                views.SetTextViewText(label, text);
            }

            views.SetOnClickPendingIntent(cell, state.IsConnected
                ? ForkIntents.ActionPendingIntent(context, ForkIntents.ActionSetNoiseMode, requestCode, (int)mode)
                : ForkIntents.OpenAppPendingIntent(context));
        }

        views.SetOnClickPendingIntent(Resource.Id.fork_controls_root, ForkIntents.OpenAppPendingIntent(context));
        return views;
    }

    /// <summary>
    /// Modes of the configured model. Before the core runs (boot, process restart) the model comes from settings.json,
    /// so the columns are right from the start. Unknown model: Off / Ambient / ANC.
    /// </summary>
    private static (bool Ambient, bool Adaptive, bool Anc) ModeSupport(BudsSnapshot state)
    {
        if (state.CoreRunning && state.HasValidDevice)
            return (state.HasAmbient, state.HasAdaptive, state.HasAnc);

        var model = BudsState.ConfiguredDevice().Model;
        return model == Models.NULL ? (true, false, true) : BudsState.ModeSupportFor(model);
    }

    private static int ModeLabel(NoiseControlModes mode) => mode switch
    {
        NoiseControlModes.AmbientSound => Resource.String.fork_nc_ambient,
        NoiseControlModes.Adaptive => Resource.String.fork_nc_adaptive,
        NoiseControlModes.NoiseReduction => Resource.String.fork_nc_anc,
        _ => Resource.String.fork_nc_off
    };

    private static void SetBattery(RemoteViews views, Context context, int bar, int text, int? level)
    {
        views.SetProgressBar(bar, 100, level ?? 0, false);
        views.SetTextViewText(text, level is { } l
            ? context.GetString(Resource.String.fork_battery_percent, l)
            : context.GetString(Resource.String.fork_battery_unknown));
    }

    // Android 12+ resolves color resources in the launcher, so the widget follows light/dark changes without a redraw
    private static void SetImageTint(RemoteViews views, Context context, int id, int colorRes)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
            views.SetColorStateList(id, "setImageTintList", colorRes);
        else
            views.SetInt(id, "setColorFilter", context.GetColor(colorRes));
    }

    private static void SetTextColor(RemoteViews views, Context context, int id, int colorRes)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
            views.SetColorStateList(id, "setTextColor", colorRes);
        else
            views.SetTextColor(id, new Color(context.GetColor(colorRes)));
    }

    public static string Title(Context context, BudsSnapshot state)
    {
        var name = state.CoreRunning ? state.DeviceName : BudsState.ConfiguredDevice().Name;
        return string.IsNullOrWhiteSpace(name) ? context.GetString(Resource.String.fork_default_device_name) : name;
    }

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
