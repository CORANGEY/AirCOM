using AirCOM.Core.Protocol;
using AirCOM.Core.Serial;

namespace AirCOM.Core.Protocol.Messages;

/// <summary>
/// Control line status change. Payload (3 bytes):
/// outputFlags[1] (bit0=DTR, bit1=RTS) | inputFlags[1] (bit0=DCD, bit1=CTS, bit2=DSR, bit3=RI)
/// | changeMask[1] (which bits actually changed and should be applied)
/// Note: the bit numbering here uses the OUTPUT/INPUT split (output in byte 0, input in byte 1),
/// distinct from <see cref="ModemStatusFlags"/> which packs both into one byte.
/// </summary>
public sealed class LineStateMessage : IMessage
{
    public FrameType Type => FrameType.LineState;

    /// <summary>Output lines: DTR / RTS.</summary>
    public ModemStatusFlags OutputLines { get; private set; }

    /// <summary>Input lines: DCD / CTS / DSR / RI.</summary>
    public ModemStatusFlags InputLines { get; private set; }

    /// <summary>Which bits changed (same layout as Output/Input bytes).</summary>
    public ModemStatusFlags ChangeMask { get; private set; }

    public LineStateMessage() { }

    public LineStateMessage(ModemStatusFlags output, ModemStatusFlags input, ModemStatusFlags changeMask)
    {
        OutputLines = output & ModemStatusFlags.OutputMask;
        InputLines = input & ModemStatusFlags.InputMask;
        ChangeMask = changeMask;
    }

    public byte[] ToPayload()
    {
        var buf = new byte[3];
        buf[0] = (byte)(OutputLines & ModemStatusFlags.OutputMask);
        buf[1] = PackInput(InputLines);
        buf[2] = (byte)ChangeMask;
        return buf;
    }

    public void FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 3)
            throw new ArgumentException($"LINE_STATE payload must be 3 bytes, got {payload.Length}.", nameof(payload));
        OutputLines = (ModemStatusFlags)(payload[0] & (byte)ModemStatusFlags.OutputMask);
        InputLines = UnpackInput(payload[1]);
        ChangeMask = (ModemStatusFlags)payload[2];
    }

    /// <summary>Maps the wire input byte (DCD/CTS/DSR/RI in bits 0..3) to <see cref="ModemStatusFlags"/>.</summary>
    private static byte PackInput(ModemStatusFlags input)
    {
        byte b = 0;
        if ((input & ModemStatusFlags.Dcd) != 0) b |= 1 << 0;
        if ((input & ModemStatusFlags.Cts) != 0) b |= 1 << 1;
        if ((input & ModemStatusFlags.Dsr) != 0) b |= 1 << 2;
        if ((input & ModemStatusFlags.Ri) != 0) b |= 1 << 3;
        return b;
    }

    private static ModemStatusFlags UnpackInput(byte b)
    {
        ModemStatusFlags f = ModemStatusFlags.None;
        if ((b & (1 << 0)) != 0) f |= ModemStatusFlags.Dcd;
        if ((b & (1 << 1)) != 0) f |= ModemStatusFlags.Cts;
        if ((b & (1 << 2)) != 0) f |= ModemStatusFlags.Dsr;
        if ((b & (1 << 3)) != 0) f |= ModemStatusFlags.Ri;
        return f;
    }

    public static IMessage Decode(ReadOnlySpan<byte> payload)
    {
        var msg = new LineStateMessage();
        msg.FromPayload(payload);
        return msg;
    }
}
