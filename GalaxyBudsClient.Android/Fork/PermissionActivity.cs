using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Widget;
using AndroidX.Core.App;

namespace GalaxyBudsClient.Android.Fork;

/// <summary>
/// Invisible activity that asks for the runtime permissions the service needs, then runs the widget action
/// that sent the user here. Services and widgets can't request permissions themselves.
/// </summary>
[Activity(
    Name = "com.andrea.galaxybudsclient.fork.PermissionActivity",
    Theme = "@android:style/Theme.Translucent.NoTitleBar",
    Exported = false,
    ExcludeFromRecents = true,
    NoHistory = true,
    TaskAffinity = "com.andrea.galaxybudsclient.permission")]
internal class PermissionActivity : Activity
{
    // Must differ from MainActivity's request code (1)
    private const int RequestCode = 4242;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var missing = ForkIntents.MissingPermissions(this);
        ForkIntents.MarkPermissionsAsked(this);
        if (missing.Length == 0)
            Continue();
        else
            ActivityCompat.RequestPermissions(this, missing, RequestCode);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == RequestCode)
            Continue();
    }

    private void Continue()
    {
        if (ForkIntents.HasBluetoothPermission(this))
        {
            var action = Intent?.GetStringExtra(ForkIntents.ExtraNextAction);
            if (action != null)
                ForkIntents.StartService(this, action);
        }
        else
        {
            Toast.MakeText(this, Resource.String.fork_permission_denied, ToastLength.Long)?.Show();
        }

        // Widgets now point straight to the service (or still here, if denied)
        WidgetRenderer.UpdateAll(this);
        Finish();
    }
}
