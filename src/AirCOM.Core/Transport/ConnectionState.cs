namespace AirCOM.Core.Transport;

/// <summary>
/// State of a transport connection.
/// </summary>
public enum ConnectionState
{
    /// <summary>Not connected and not attempting to connect.</summary>
    Disconnected,

    /// <summary>A connect attempt is in progress.</summary>
    Connecting,

    /// <summary>Connected and ready for I/O.</summary>
    Connected,

    /// <summary>Shutting down.</summary>
    Disconnecting,
}
