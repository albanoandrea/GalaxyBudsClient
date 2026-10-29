using FluentAssertions;
using GalaxyBudsClient.Message;
using GalaxyBudsClient.Message.Decoder;
using GalaxyBudsClient.Model.Constants;

namespace GalaxyBudsClient.Tests.Buds4Pro;

/// <summary>
/// Fork: payloads captured from a Buds4 Pro (2026-10-29) after setting the touch-and-hold
/// noise control modes and "extra clear call sound" in Samsung's app.
/// </summary>
[TestFixture, Description("Test ExtendedStatusUpdate parser for the Buds4 Pro"), TestOf(typeof(ExtendedStatusUpdateDecoder))]
public class ExtendedStatusUpdateTests
{
    // Left: ANC + Off, right: Ambient + Adaptive (byte 21 = 0xC3), extra clear call sound on, worn
    private const string ClearCallOnPayload =
        "040D636301011159 0000CF2201006701 6701070004C30004 0410000000002202 0000010002EA0000 0800000001000101 0D000001 00FF010300000000 0001";

    // Left: ANC + Adaptive, right: Ambient + Off (byte 21 = 0xA5), extra clear call sound off, both in the case
    private const string ClearCallOffChargingPayload =
        "040D636201013359 0000CF2200006701 6701070004A50004 0410000000002202 0000010002EA0000 0800001400000101 0D000001 00FF010300000000 0001";

    private static ExtendedStatusUpdateDecoder Decode(string hex)
    {
        var payload = Convert.FromHexString(hex.Replace(" ", ""));
        var msg = new SppMessage(MsgIds.EXTENDED_STATUS_UPDATED, MsgTypes.Response, payload, Models.Buds4Pro);
        return (ExtendedStatusUpdateDecoder)msg.CreateDecoder()!;
    }

    [Test]
    public void DecodesTouchAndHoldModesAndClearCall()
    {
        var e = Decode(ClearCallOnPayload);

        e.NoiseControlTouchLeftAnc.Should().BeTrue();
        e.NoiseControlTouchLeftOff.Should().BeTrue();
        e.NoiseControlTouchLeftAmbient.Should().BeFalse();
        e.NoiseControlTouchLeftAdaptive.Should().BeFalse();

        e.NoiseControlTouchAmbient.Should().BeTrue();
        e.NoiseControlTouchAdaptive.Should().BeTrue();
        e.NoiseControlTouchAnc.Should().BeFalse();
        e.NoiseControlTouchOff.Should().BeFalse();

        e.ExtraClearCallSound.Should().BeTrue();
        e.IsLeftCharging.Should().BeFalse();
        e.IsRightCharging.Should().BeFalse();
    }

    [Test]
    public void DecodesChargingStateAndClearCallOff()
    {
        var e = Decode(ClearCallOffChargingPayload);

        e.NoiseControlTouchLeftAnc.Should().BeTrue();
        e.NoiseControlTouchLeftAdaptive.Should().BeTrue();
        e.NoiseControlTouchLeftOff.Should().BeFalse();
        e.NoiseControlTouchAmbient.Should().BeTrue();
        e.NoiseControlTouchOff.Should().BeTrue();
        e.NoiseControlTouchAdaptive.Should().BeFalse();

        e.ExtraClearCallSound.Should().BeFalse();
        e.IsLeftCharging.Should().BeTrue();
        e.IsRightCharging.Should().BeTrue();
    }
}
