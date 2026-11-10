using System.ComponentModel;
using GalaxyBudsClient.Message;
using GalaxyBudsClient.Message.Decoder;
using GalaxyBudsClient.Model.Specifications;
using GalaxyBudsClient.Platform;

namespace GalaxyBudsClient.Interface.ViewModels.Pages;

/// <summary>
/// Fork: single "Media controls" switch for the Buds4 generation (<see cref="Features.MediaControlsSwitch"/>),
/// like Samsung's app. It drives single, double and triple pinch plus the volume swipe together; Samsung's
/// switch clears exactly these four bits of the gesture mask (captured on a Buds4 Pro, see
/// docs/buds4pro-samsung-alignment.md). The per-gesture switches are hidden on these models.
/// </summary>
public partial class TouchpadPageViewModel
{
    private bool _isApplyingMediaControlsState;

    // Not exposed in the UI on its own; sent with every LOCK_TOUCHPAD
    private bool _isSwipeGestureEnabled = true;

    private static bool IsMediaControlsSwitchSupported =>
        BluetoothImpl.Instance.DeviceSpec.Supports(Features.MediaControlsSwitch);

    private void InitMediaControls()
    {
        // Subscribed after OnExtendedStatusUpdate, so the per-gesture properties are already up to date
        SppMessageReceiver.Instance.ExtendedStatusUpdate += OnMediaControlsStatusUpdate;
        PropertyChanged += OnMediaControlsPropertyChanged;
    }

    private void OnMediaControlsStatusUpdate(object? sender, ExtendedStatusUpdateDecoder e)
    {
        if (!IsMediaControlsSwitchSupported)
            return;

        _isSwipeGestureEnabled = e.SwipeOn;

        // Mixed states (possible when set by another app) show as on; toggling makes them uniform
        _isApplyingMediaControlsState = true;
        IsMediaControlsEnabled = e.SingleTapOn || e.DoubleTapOn || e.TripleTapOn || e.SwipeOn;
        _isApplyingMediaControlsState = false;
    }

    private void OnMediaControlsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(IsMediaControlsEnabled) ||
            _isApplyingMediaControlsState || !IsMediaControlsSwitchSupported)
            return;

        using (SuppressChangeNotifications())
        {
            IsSingleTapGestureEnabled = IsMediaControlsEnabled;
            IsDoubleTapGestureEnabled = IsMediaControlsEnabled;
            IsTripleTapGestureEnabled = IsMediaControlsEnabled;
        }
        _isSwipeGestureEnabled = IsMediaControlsEnabled;

        // Sends one LOCK_TOUCHPAD with the full gesture state
        OnPropertyChanged(null, new PropertyChangedEventArgs(nameof(IsSingleTapGestureEnabled)));
    }

    [Reactive] private bool _isMediaControlsEnabled;
}
