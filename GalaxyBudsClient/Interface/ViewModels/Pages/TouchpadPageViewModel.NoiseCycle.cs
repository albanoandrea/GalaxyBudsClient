using System.ComponentModel;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia.Threading;
using GalaxyBudsClient.Message;
using GalaxyBudsClient.Message.Decoder;
using GalaxyBudsClient.Model.Specifications;
using GalaxyBudsClient.Platform;

namespace GalaxyBudsClient.Interface.ViewModels.Pages;

/// <summary>
/// Per-side checklist of the noise control modes cycled by touch and hold (Buds3 Pro / Buds4 Pro,
/// <see cref="Features.NoiseControlAdaptive"/>), like Samsung's "Switch noise controls" screen.
/// SET_TOUCH_AND_HOLD_NOISE_CONTROLS takes one byte per side [left, right] with a bit per mode;
/// the extended status update reports the same bits in one byte (right: low nibble, left: high nibble).
/// The legacy pair combo boxes (<see cref="NoiseControlCycleMode"/>) can't express Adaptive and are hidden on these models.
/// </summary>
public partial class TouchpadPageViewModel
{
    private const byte CycleAmbient = 1;
    private const byte CycleAdaptive = 2;
    private const byte CycleOff = 4;
    private const byte CycleAnc = 8;

    // The earbuds require at least two modes per side
    private const int MinimumCycleModes = 2;

    private bool _isApplyingCycleState;

    private static bool IsNoiseCycleChecklistSupported =>
        BluetoothImpl.Instance.DeviceSpec.Supports(Features.NoiseControlAdaptive);

    private byte LeftCycleBits => ToCycleBits(IsCycleLeftAnc, IsCycleLeftAdaptive, IsCycleLeftAmbient, IsCycleLeftOff);
    private byte RightCycleBits => ToCycleBits(IsCycleRightAnc, IsCycleRightAdaptive, IsCycleRightAmbient, IsCycleRightOff);

    private void InitNoiseCycleChecklist()
    {
        SppMessageReceiver.Instance.ExtendedStatusUpdate += OnNoiseCycleStatusUpdate;
        PropertyChanged += OnNoiseCyclePropertyChanged;
    }

    private void OnNoiseCycleStatusUpdate(object? sender, ExtendedStatusUpdateDecoder e)
    {
        if (!IsNoiseCycleChecklistSupported)
            return;

        _isApplyingCycleState = true;
        IsCycleLeftAnc = e.NoiseControlTouchLeftAnc;
        IsCycleLeftAdaptive = e.NoiseControlTouchLeftAdaptive;
        IsCycleLeftAmbient = e.NoiseControlTouchLeftAmbient;
        IsCycleLeftOff = e.NoiseControlTouchLeftOff;
        IsCycleRightAnc = e.NoiseControlTouchAnc;
        IsCycleRightAdaptive = e.NoiseControlTouchAdaptive;
        IsCycleRightAmbient = e.NoiseControlTouchAmbient;
        IsCycleRightOff = e.NoiseControlTouchOff;
        _isApplyingCycleState = false;
    }

    private async void OnNoiseCyclePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_isApplyingCycleState || !IsNoiseCycleChecklistSupported)
            return;

        switch (args.PropertyName)
        {
            case nameof(IsCycleLeftAnc):
            case nameof(IsCycleLeftAdaptive):
            case nameof(IsCycleLeftAmbient):
            case nameof(IsCycleLeftOff):
            case nameof(IsCycleRightAnc):
            case nameof(IsCycleRightAdaptive):
            case nameof(IsCycleRightAmbient):
            case nameof(IsCycleRightOff):
                var changedBits = args.PropertyName.StartsWith("IsCycleLeft") ? LeftCycleBits : RightCycleBits;
                if (BitOperations.PopCount(changedBits) < MinimumCycleModes)
                {
                    // Not a valid selection: re-check the box that was just cleared
                    Dispatcher.UIThread.Post(() => RevertCycleMode(args.PropertyName));
                    return;
                }

                await SendNoiseCycleChecklistAsync();
                break;
        }
    }

    private void RevertCycleMode(string name)
    {
        _isApplyingCycleState = true;
        switch (name)
        {
            case nameof(IsCycleLeftAnc): IsCycleLeftAnc = true; break;
            case nameof(IsCycleLeftAdaptive): IsCycleLeftAdaptive = true; break;
            case nameof(IsCycleLeftAmbient): IsCycleLeftAmbient = true; break;
            case nameof(IsCycleLeftOff): IsCycleLeftOff = true; break;
            case nameof(IsCycleRightAnc): IsCycleRightAnc = true; break;
            case nameof(IsCycleRightAdaptive): IsCycleRightAdaptive = true; break;
            case nameof(IsCycleRightAmbient): IsCycleRightAmbient = true; break;
            case nameof(IsCycleRightOff): IsCycleRightOff = true; break;
        }
        _isApplyingCycleState = false;
    }

    private async Task SendNoiseCycleChecklistAsync()
    {
        await BluetoothImpl.Instance.SendRequestAsync(MsgIds.SET_TOUCH_AND_HOLD_NOISE_CONTROLS,
            LeftCycleBits, RightCycleBits);
    }

    private static byte ToCycleBits(bool anc, bool adaptive, bool ambient, bool off) =>
        (byte)((anc ? CycleAnc : 0) | (adaptive ? CycleAdaptive : 0) | (ambient ? CycleAmbient : 0) | (off ? CycleOff : 0));

    [Reactive] private bool _isCycleLeftAnc;
    [Reactive] private bool _isCycleLeftAdaptive;
    [Reactive] private bool _isCycleLeftAmbient;
    [Reactive] private bool _isCycleLeftOff;
    [Reactive] private bool _isCycleRightAnc;
    [Reactive] private bool _isCycleRightAdaptive;
    [Reactive] private bool _isCycleRightAmbient;
    [Reactive] private bool _isCycleRightOff;
}
