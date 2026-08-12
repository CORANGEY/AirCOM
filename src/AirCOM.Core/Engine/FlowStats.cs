using AirCOM.Core.Protocol;
using AirCOM.Core.Serial;
using AirCOM.Core.Transport;
using Microsoft.Extensions.Logging;

namespace AirCOM.Core.Engine;

/// <summary>
/// Flow statistics reported periodically by <see cref="SerialBridge"/>.
/// </summary>
public sealed class FlowStats
{
    /// <summary>Bytes received from the serial port (and forwarded to the network).</summary>
    public long BytesSerialToNet { get; internal set; }

    /// <summary>Bytes received from the network (and written to the serial port).</summary>
    public long BytesNetToSerial { get; internal set; }

    /// <summary>Total frames sent over the network.</summary>
    public long FramesSent { get; internal set; }

    /// <summary>Total frames received from the network.</summary>
    public long FramesReceived { get; internal set; }

    /// <summary>When the bridge started (UTC ticks, supplied by caller to avoid Date in tests).</summary>
    public long StartedTicks { get; internal set; }
}
