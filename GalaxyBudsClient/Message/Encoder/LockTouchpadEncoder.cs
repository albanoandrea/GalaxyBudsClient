using System.IO;
using GalaxyBudsClient.Generated.Model.Attributes;
using GalaxyBudsClient.Model.Constants;
using GalaxyBudsClient.Model.Specifications;

namespace GalaxyBudsClient.Message.Encoder;

[MessageEncoder(MsgIds.LOCK_TOUCHPAD)]
public class LockTouchpadEncoder : BaseMessageEncoder
{
    public bool LockAll { get; init; }
    public bool TapOn { get; init; }
    public bool DoubleTapOn { get; init; }
    public bool TripleTapOn { get; init; }
    public bool HoldTapOn { get; init; }
    public bool DoubleTapCallOn { get; init; }
    public bool HoldTapCallOn { get; init; }

    /// <summary>
    /// Buds4 only: current lighting style byte. It occupies the trailing
    /// "earbuds control" payload and must be echoed back unmodified to
    /// avoid resetting the blade light configuration.
    /// </summary>
    public byte Lighting { get; init; }

    /// <summary>
    /// Buds4 only: quick launch (double pinch and hold) state byte.
    /// Echoed back unmodified like <see cref="Lighting"/>.
    /// </summary>
    public byte QuickLaunchAdvanced { get; init; }

    /// <summary>
    /// Buds4 only: bit 7 of the ESU touch mask (meaning unknown, always set
    /// in captures), echoed back unmodified.
    /// </summary>
    public byte GestureEchoBits { get; init; }

    /// <summary>Buds4 only: swipe up/down for volume.</summary>
    public bool SwipeOn { get; init; }

    public override SppMessage Encode()
    {
        if (DeviceSpec.Device >= Models.Buds4)
        {
            // Buds4 "earbuds control" layout, verified on a Buds4 Pro by
            // toggling each byte and reading the gesture mask back (ESU [10]
            // and TOUCH_ON_BUDS [5]); it matches the legacy bit order:
            // [0] reserved, [1] single tap (bit3), [2] double tap (bit2),
            // [3] triple tap (bit1), [4] pinch and hold (bit0), [5] double
            // tap during call (bit4), [6] pinch and hold during call (bit5),
            // [7] volume swipe (bit6), [8] and [9] no visible effect (sent as
            // in the captures), [10] lighting style, [11] quick launch
            return new SppMessage(MsgIds.LOCK_TOUCHPAD, MsgTypes.Request,
            [
                (byte)0,
                (byte)(TapOn ? 1 : 0),
                (byte)(DoubleTapOn ? 1 : 0),
                (byte)(TripleTapOn ? 1 : 0),
                (byte)(HoldTapOn ? 1 : 0),
                (byte)(DoubleTapCallOn ? 1 : 0),
                (byte)(HoldTapCallOn ? 1 : 0),
                (byte)(SwipeOn ? 1 : 0),
                (byte)((GestureEchoBits & 0x80) != 0 ? 1 : 0),
                (byte)(HoldTapCallOn ? 1 : 0),
                Lighting,
                QuickLaunchAdvanced
            ]);
        }

        using var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);

        writer.Write(!LockAll);
        writer.Write(TapOn);
        writer.Write(DoubleTapOn);
        writer.Write(TripleTapOn);
        writer.Write(HoldTapOn);

        if (DeviceSpec.Supports(Features.AdvancedTouchLockForCalls))
        {
            writer.Write(DoubleTapCallOn);
            writer.Write(HoldTapCallOn);
        }
            
        return new SppMessage(MsgIds.LOCK_TOUCHPAD, MsgTypes.Request, stream.ToArray());
    }
}