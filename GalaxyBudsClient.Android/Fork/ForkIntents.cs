using System;
using System.Collections.Generic;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using AndroidX.Core.Content;

namespace GalaxyBudsClient.Android.Fork;

/// <summary>Service actions and the PendingIntents that widgets/notifications use to trigger them.</summary>
internal static class ForkIntents
{
    private const string Prefix = "com.andrea.galaxybudsclient.action.";
    public const string ActionConnect = Prefix + "CONNECT";
    public const string ActionDisconnect = Prefix + "DISCONNECT";
    /// <summary>Keep the service alive for a connection made elsewhere (e.g. the app UI)</summary>
    public const string ActionAttach = Prefix + "ATTACH";
    /// <summary>Set the noise control mode in <see cref="ExtraNoiseMode"/> (a NoiseControlModes value)</summary>
    public const string ActionSetNoiseMode = Prefix + "SET_NOISE_MODE";
    public const string ExtraNoiseMode = "com.andrea.galaxybudsclient.extra.NOISE_MODE";

    // PendingIntent request codes (one per button, so they never replace each other)
    public const int RequestWidgetConnect = 1;
    public const int RequestWidgetDisconnect = 2;
    public const int RequestNotificationConnect = 3;
    public const int RequestNotificationDisconnect = 4;
    public const int RequestWidgetModeOff = 10;
    public const int RequestWidgetModeAmbient = 11;
    public const int RequestWidgetModeAdaptive = 12;
    public const int RequestWidgetModeAnc = 13;

    public const string ExtraNextAction = "com.andrea.galaxybudsclient.extra.NEXT_ACTION";

    /// <summary>Runtime permissions the service needs (Bluetooth is mandatory, notifications are optional)</summary>
    public static string[] MissingPermissions(Context context)
    {
        var missing = new List<string>();
        if (OperatingSystem.IsAndroidVersionAtLeast(31) && !IsGranted(context, Manifest.Permission.BluetoothConnect))
            missing.Add(Manifest.Permission.BluetoothConnect);
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && !IsGranted(context, Manifest.Permission.PostNotifications))
            missing.Add(Manifest.Permission.PostNotifications);
        return missing.ToArray();
    }

    /// <summary>Route through <see cref="PermissionActivity"/>: Bluetooth missing, or notifications never asked for</summary>
    public static bool NeedsPermissionScreen(Context context) =>
        !HasBluetoothPermission(context) ||
        (MissingPermissions(context).Length > 0 && !Prefs(context).GetBoolean(PrefPermissionsAsked, false));

    public static void MarkPermissionsAsked(Context context) =>
        Prefs(context).Edit()!.PutBoolean(PrefPermissionsAsked, true)!.Apply();

    private const string PrefPermissionsAsked = "permissions_asked";
    private static ISharedPreferences Prefs(Context context) =>
        context.GetSharedPreferences("fork_android", FileCreationMode.Private)!;

    public static bool HasBluetoothPermission(Context context) =>
        !OperatingSystem.IsAndroidVersionAtLeast(31) || IsGranted(context, Manifest.Permission.BluetoothConnect);

    private static bool IsGranted(Context context, string permission) =>
        ContextCompat.CheckSelfPermission(context, permission) == Permission.Granted;

    public static Intent ServiceIntent(Context context, string action) =>
        new Intent(context, typeof(BudsConnectionService)).SetAction(action);

    /// <summary>Starts the foreground service; only allowed while the app is visible or from a user-initiated PendingIntent.</summary>
    public static void StartService(Context context, string action) =>
        ContextCompat.StartForegroundService(context, ServiceIntent(context, action));

    /// <summary>
    /// PendingIntent for a widget/notification button. Goes straight to the service when the permissions are in place
    /// (widget clicks are allowed to start foreground services from the background), otherwise to the permission screen first.
    /// </summary>
    public static PendingIntent ActionPendingIntent(Context context, string action, int requestCode, int? noiseMode = null)
    {
        const PendingIntentFlags flags = PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable;

        if (NeedsPermissionScreen(context))
        {
            var intent = new Intent(context, typeof(PermissionActivity))
                .PutExtra(ExtraNextAction, action)
                .AddFlags(ActivityFlags.NewTask);
            return PendingIntent.GetActivity(context, requestCode, intent, flags)!;
        }

        var serviceIntent = ServiceIntent(context, action);
        if (noiseMode != null)
            serviceIntent.PutExtra(ExtraNoiseMode, noiseMode.Value);
        return OperatingSystem.IsAndroidVersionAtLeast(26)
            ? PendingIntent.GetForegroundService(context, requestCode, serviceIntent, flags)!
            : PendingIntent.GetService(context, requestCode, serviceIntent, flags)!;
    }

    /// <summary>Opens the app like the launcher does (brings the existing task to front)</summary>
    public static PendingIntent OpenAppPendingIntent(Context context)
    {
        var intent = context.PackageManager!.GetLaunchIntentForPackage(context.PackageName!)
                     ?? new Intent(context, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ResetTaskIfNeeded);
        return PendingIntent.GetActivity(context, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }
}
