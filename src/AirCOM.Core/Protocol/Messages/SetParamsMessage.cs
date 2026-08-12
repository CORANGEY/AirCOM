using AirCOM.Core.Protocol;

namespace AirCOM.Core.Protocol.Messages;

/// <summary>
/// Set serial port parameters. Payload (8 bytes):
/// baud[4] (big-endian, 0xFFFFFFFF = unknown) | dataBits[1] | stopBits[1] | parity[1] | flowControl[1]
/// Field encodings align with RFC 2217 Com Port Option semantics.
/// </summary>
public sealed class SetParamsMessage : IMessage
{
    public FrameType Type => FrameType.SetParams;

    public SerialParams Params { get; private set; }

    public SetParamsMessage() { Params = SerialParams.Default; }

    public SetParamsMessage(SerialParams parameters) { Params = parameters; }

    public byte[] ToPayload()
    {
        var buf = new byte[8];
        PayloadWriter.WriteUInt32(buf, 0, Params.BaudRate);
        buf[4] = Params.DataBits;
        buf[5] = (byte)Params.StopBits;
        buf[6] = (byte)Params.Parity;
        buf[7] = (byte)Params.FlowControl;
        return buf;
    }

    public void FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
            throw new ArgumentException($"SET_PARAMS payload must be 8 bytes, got {payload.Length}.", nameof(payload));
        Params = new SerialParams(
            BaudRate: PayloadReader.ReadUInt32(payload, 0),
            DataBits: payload[4],
            StopBits: (StopBitsKind)payload[5],
            Parity: (Parity)payload[6],
            FlowControl: (FlowControl)payload[7]);
    }

    public static IMessage Decode(ReadOnlySpan<byte> payload)
    {
        var msg = new SetParamsMessage();
        msg.FromPayload(payload);
        return msg;
    }
}
