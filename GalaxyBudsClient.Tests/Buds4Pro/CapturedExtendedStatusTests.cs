using FluentAssertions;
using GalaxyBudsClient.Message;
using GalaxyBudsClient.Message.Decoder;
using GalaxyBudsClient.Model.Constants;

namespace GalaxyBudsClient.Tests.Buds4Pro;

/// <summary>
/// Fork: payloads captured from a Buds4 Pro (2026-10-29) after setting the touch-and-hold
/// noise control modes and "extra clear call sound" in Samsung's app.
/// </summary>
[TestFixture, Description("Fork: ExtendedStatusUpdate payloads captured from a Buds4 Pro"), TestOf(typeof(ExtendedStatusUpdateDecoder))]
public class CapturedExtendedStatusTests
{
    // Left: ANC + Off, right: Ambient + Adaptive (byte 21 = 0xC3), extra clear call sound on, worn
    private const string ClearCallOnPayload =
        "040D636301011159 0000CF2201006701 6701070004C30004 0410000000002202 0000010002EA0000 0800000001000101 0D000001 00FF010300000000 0001";

    // Left: ANC + Adaptive, right: Ambient + Off (byte 21 = 0xA5), extra clear call sound off, both in the case
    private const string ClearCallOffChargingPayload =
        "040D636201013359 0000CF2200006701 6701070004A50004 0410000000002202 0000010002EA0000 0800001400000101 0D000001 00FF010300000000 0001";

    // 2026-11-10, gesture mask (byte 10) = 0xB1 written by Samsung's app: "Media controls" off
    // (single, double, triple pinch and volume swipe), pinch and hold on, answer call and decline call on
    private const string MediaControlsOffPayload =
        "040D626301011100 0000B12200006701 6701070004C50000 0414000000002202 0000010102EA0000 0800000001000100 0D000000 00FF010300000000 0001";

    // 2026-11-10, gesture mask = 0xEF after LOCK_TOUCHPAD 00 01 01 01 01 00 01 01 01 01 01 00:
    // everything on except double pinch during calls (byte [5] of the request)
    private const string AnswerCallOffPayload =
        "040D636401011100 0000EF2200006701 6701070004C50000 0414000000002202 0000010102EA0000 0800000001000100 0D000001 00FF010300000000 0001";

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

    [Test]
    public void DecodesGestureMaskWithMediaControlsOff()
    {
        var e = Decode(MediaControlsOffPayload);

        e.SingleTapOn.Should().BeFalse();
        e.DoubleTapOn.Should().BeFalse();
        e.TripleTapOn.Should().BeFalse();
        e.SwipeOn.Should().BeFalse();
        e.TouchHoldOn.Should().BeTrue();
        e.DoubleTapForCallOn.Should().BeTrue();
        e.TouchHoldOnForCallOn.Should().BeTrue();
        e.TouchpadLock.Should().BeFalse();
        e.GestureEchoBits.Should().Be(0x80);
    }

    [Test]
    public void DecodesGestureMaskWithAnswerCallOff()
    {
        var e = Decode(AnswerCallOffPayload);

        e.SingleTapOn.Should().BeTrue();
        e.DoubleTapOn.Should().BeTrue();
        e.TripleTapOn.Should().BeTrue();
        e.SwipeOn.Should().BeTrue();
        e.TouchHoldOn.Should().BeTrue();
        e.DoubleTapForCallOn.Should().BeFalse();
        e.TouchHoldOnForCallOn.Should().BeTrue();
    }
}
