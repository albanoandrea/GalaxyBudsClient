using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;
using GalaxyBudsClient.Message;
using GalaxyBudsClient.Message.Decoder;
using GalaxyBudsClient.Model.Config;
using GalaxyBudsClient.Model.Specifications;
using GalaxyBudsClient.Platform;

namespace GalaxyBudsClient.Interface.MarkupExtensions;

/// <summary>
/// Fork: like <see cref="RequiresFeatureBehavior"/>, but also hides the control if the device supports <see cref="Without"/>.
/// Used to replace an upstream control with a fork one on newer models (a second behavior would race for IsVisible).
/// </summary>
public class RequiresFeatureWithoutBehavior : Behavior<Control>
{
    public static readonly StyledProperty<Features> FeatureProperty =
        AvaloniaProperty.Register<RequiresFeatureWithoutBehavior, Features>(nameof(Feature));

    public static readonly StyledProperty<Features> WithoutProperty =
        AvaloniaProperty.Register<RequiresFeatureWithoutBehavior, Features>(nameof(Without));

    public Features Feature
    {
        get => GetValue(FeatureProperty);
        set => SetValue(FeatureProperty, value);
    }

    public Features Without
    {
        get => GetValue(WithoutProperty);
        set => SetValue(WithoutProperty, value);
    }

    protected override void OnAttachedToVisualTree()
    {
        UpdateState();
        BluetoothImpl.Instance.Device.DeviceChanged += OnDeviceChanged;
        SppMessageReceiver.Instance.ExtendedStatusUpdate += OnExtendedStatusUpdate;
    }

    protected override void OnDetachedFromVisualTree()
    {
        SppMessageReceiver.Instance.ExtendedStatusUpdate -= OnExtendedStatusUpdate;
        BluetoothImpl.Instance.Device.DeviceChanged -= OnDeviceChanged;
    }

    private void OnExtendedStatusUpdate(object? sender, ExtendedStatusUpdateDecoder e) => UpdateState();
    private void OnDeviceChanged(object? sender, Device? e) => UpdateState();

    private bool State => BluetoothImpl.Instance.DeviceSpec.Supports(Feature) &&
                          !BluetoothImpl.Instance.DeviceSpec.Supports(Without);

    private void UpdateState()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (AssociatedObject is null)
                return;

            AssociatedObject.IsVisible = State;
        }, DispatcherPriority.Normal);
    }
}
