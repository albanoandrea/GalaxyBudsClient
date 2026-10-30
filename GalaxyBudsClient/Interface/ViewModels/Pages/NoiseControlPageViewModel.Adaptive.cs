using System.ComponentModel;
using GalaxyBudsClient.Message;
using GalaxyBudsClient.Message.Decoder;
using GalaxyBudsClient.Message.Parameter;
using GalaxyBudsClient.Model;
using GalaxyBudsClient.Model.Constants;
using GalaxyBudsClient.Model.Specifications;
using GalaxyBudsClient.Platform;

namespace GalaxyBudsClient.Interface.ViewModels.Pages;

/// <summary>
/// Adaptive noise control mode (NOISE_CONTROLS = 3, Buds3 Pro / Buds4 Pro, <see cref="Features.NoiseControlAdaptive"/>).
/// Upstream only knows Off / Ambient / ANC and leaves the previous switch on when the earbuds report Adaptive
/// (e.g. after a touch-and-hold cycle). The three switches are mutually exclusive.
/// </summary>
public partial class NoiseControlPageViewModel
{
    private static bool IsAdaptiveSupported =>
        BluetoothImpl.Instance.DeviceSpec.Supports(Features.NoiseControlAdaptive);

    private void InitAdaptiveMode()
    {
        SppMessageReceiver.Instance.ExtendedStatusUpdate += OnAdaptiveStatusUpdate;
        SppMessageReceiver.Instance.NoiseControlUpdateResponse += (_, mode) => ApplyAdaptiveMode(mode);
        SppMessageReceiver.Instance.AcknowledgementResponse += OnAdaptiveAcknowledgement;
        PropertyChanged += OnAdaptivePropertyChanged;
    }

    private void OnAdaptiveStatusUpdate(object? sender, ExtendedStatusUpdateDecoder e) => ApplyAdaptiveMode(e.NoiseControlMode);

    private void OnAdaptiveAcknowledgement(object? sender, AcknowledgementDecoder ack)
    {
        if (ack.Id == MsgIds.NOISE_CONTROLS && ack.Parameters is SimpleAckParameter param)
            ApplyAdaptiveMode((NoiseControlModes)param.Value);
    }

    private void ApplyAdaptiveMode(NoiseControlModes mode)
    {
        if (!IsAdaptiveSupported)
            return;

        _suppressorCounter++;
        IsAdaptiveEnabled = mode == NoiseControlModes.Adaptive;
        if (IsAdaptiveEnabled)
        {
            IsAncEnabled = false;
            IsAmbientSoundEnabled = false;
        }
        _suppressorCounter--;
    }

    private async void OnAdaptivePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_suppressorCounter > 0 || !IsAdaptiveSupported)
            return;

        switch (args.PropertyName)
        {
            case nameof(IsAdaptiveEnabled):
                _suppressorCounter++;
                if (IsAdaptiveEnabled)
                {
                    IsAncEnabled = false;
                    IsAmbientSoundEnabled = false;
                }
                _suppressorCounter--;

                await BluetoothImpl.Instance.SendRequestAsync(MsgIds.NOISE_CONTROLS,
                    (byte)(IsAdaptiveEnabled ? NoiseControlModes.Adaptive : NoiseControlModes.Off));
                EventDispatcher.Instance.Dispatch(Event.UpdateTrayIcon);
                break;
            case nameof(IsAncEnabled) or nameof(IsAmbientSoundEnabled):
                // Upstream sends the new mode; only clear the Adaptive switch here
                if (IsAncEnabled || IsAmbientSoundEnabled)
                    SetAdaptiveSilently(false);
                break;
        }
    }

    private void SetAdaptiveSilently(bool enabled)
    {
        _suppressorCounter++;
        IsAdaptiveEnabled = enabled;
        _suppressorCounter--;
    }

    [Reactive] private bool _isAdaptiveEnabled;
}
