using System.IO;
using GalaxyBudsClient.Model.Attributes;
using GalaxyBudsClient.Model.Constants;

namespace GalaxyBudsClient.Message.Parameter;

public class LockTouchpadAckParameter : MessageAsDictionary, IAckParameter
{
    public bool TouchpadLock { get; }
    
    public bool TapOn { get; }
    public bool DoubleTapOn { get; }
    public bool TripleTapOn { get; }
    public bool TouchAndHoldOn { get; }
    public bool ForCallDoubleTap { get; }
    public bool ForCallTouchAndHold { get; }
    
    public bool SupportsAdvancedTouchLock { get; }

    /// <summary>
    /// Buds4 only: current lighting style byte (trailing "earbuds control" field).
    /// </summary>
    [Device(Models.Buds4, Selector.GreaterEqual)]
    public byte? Lighting { get; }

    /// <summary>Buds4 only: quick launch (double pinch and hold) state byte.</summary>
    [Device(Models.Buds4, Selector.GreaterEqual)]
    public byte? QuickLaunchAdvanced { get; }

    public LockTouchpadAckParameter(BinaryReader reader)
    {
        if (DeviceSpec.Device >= Models.Buds4)
        {
            // Buds4 echoes the full 12-byte "earbuds control" payload:
            // [0] reserved, [1] single tap, [2] double tap, [3] triple tap,
            // [4] pinch and hold, [5] double tap during call, [6] pinch and
            // hold during call, [7] volume swipe, [8..9] unknown,
            // [10] lighting style, [11] quick launch advanced.
            // There is no touchpad lock flag on the Buds4 generation.
            try
            {
                reader.ReadByte(); // reserved
                TapOn = reader.ReadBoolean();
                DoubleTapOn = reader.ReadBoolean();
                TripleTapOn = reader.ReadBoolean();
                TouchAndHoldOn = reader.ReadBoolean();
                SupportsAdvancedTouchLock = true;
                ForCallDoubleTap = reader.ReadBoolean(); // [5]
                ForCallTouchAndHold = reader.ReadBoolean(); // [6]
                reader.ReadBoolean(); // [7] volume swipe
                reader.ReadBoolean(); // [8] unknown
                reader.ReadBoolean(); // [9] unknown
                Lighting = reader.ReadByte();
                QuickLaunchAdvanced = reader.ReadByte();
            }
            catch (EndOfStreamException)
            {
                // Partial payload; keep what was read
            }

            return;
        }

        TouchpadLock = reader.ReadBoolean();
        
        try {
            TapOn = reader.ReadBoolean();
            DoubleTapOn = reader.ReadBoolean();
            TripleTapOn = reader.ReadBoolean();
            TouchAndHoldOn = reader.ReadBoolean();
            ForCallDoubleTap = reader.ReadBoolean();
            ForCallTouchAndHold = reader.ReadBoolean();
            SupportsAdvancedTouchLock = true;
        }
        catch (EndOfStreamException) {
            SupportsAdvancedTouchLock = false;
        }
    }
}