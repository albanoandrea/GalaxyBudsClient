using System;
using System.ComponentModel;
using GalaxyBudsClient.Generated.I18N;
using GalaxyBudsClient.Message;
using GalaxyBudsClient.Message.Decoder;
using GalaxyBudsClient.Message.Parameter;
using GalaxyBudsClient.Model.Specifications;
using GalaxyBudsClient.Platform;
using ReactiveUI;

namespace GalaxyBudsClient.Interface.ViewModels.Pages;

/// <summary>
/// Multi-step ANC level (Buds3 Pro / Buds4 Pro, <see cref="Features.AncNoiseReductionLevelSlider"/>):
/// NOISE_REDUCTION_LEVEL carries a level 0..4 instead of the legacy high/low boolean (<see cref="IsAncLevelHigh"/>).
/// The current level is reported in the extended status update (NoiseReductionLevel) and echoed by the ack.
/// </summary>
public partial class NoiseControlPageViewModel
{
    public int MaximumAncLevel => 4;
    public string AncLevelDescription => string.Format(Strings.NcAncLevelValue, AncLevel + 1);

    private static bool IsAncLevelSliderSupported =>
        BluetoothImpl.Instance.DeviceSpec.Supports(Features.AncNoiseReductionLevelSlider);

    private void InitAncLevelSlider()
    {
        SppMessageReceiver.Instance.ExtendedStatusUpdate += OnAncLevelStatusUpdate;
        SppMessageReceiver.Instance.AcknowledgementResponse += OnAncLevelAcknowledgement;
        PropertyChanged += OnAncLevelPropertyChanged;
    }

    private void OnAncLevelStatusUpdate(object? sender, ExtendedStatusUpdateDecoder e)
    {
        if (IsAncLevelSliderSupported)
            SetAncLevelSilently(e.NoiseReductionLevel);
    }

    private void OnAncLevelAcknowledgement(object? sender, AcknowledgementDecoder ack)
    {
        if (ack.Id == MsgIds.NOISE_REDUCTION_LEVEL && ack.Parameters is SimpleAckParameter param && IsAncLevelSliderSupported)
            SetAncLevelSilently(param.Value);
    }

    private void SetAncLevelSilently(int level)
    {
        _suppressorCounter++;
        AncLevel = Math.Clamp(level, 0, MaximumAncLevel);
        _suppressorCounter--;
    }

    private async void OnAncLevelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(AncLevel))
            return;

        this.RaisePropertyChanged(nameof(AncLevelDescription));
        if (_suppressorCounter > 0 || !IsAncLevelSliderSupported)
            return;

        await BluetoothImpl.Instance.SendRequestAsync(MsgIds.NOISE_REDUCTION_LEVEL, (byte)Math.Clamp(AncLevel, 0, MaximumAncLevel));
    }

    [Reactive] private int _ancLevel;
}
