using System.IO.Ports;
using Microsoft.Extensions.Logging;

namespace AirCOM.Core.Serial;

/// <summary>
/// Enumerates local serial ports available on the machine.
/// </summary>
public static class SerialPortEnumerator
{
    /// <summary>Returns the names of all COM ports visible to the OS (e.g. "COM1", "COM3").</summary>
    public static IReadOnlyList<string> GetPortNames(ILogger? logger = null)
    {
        try
        {
            return SerialPort.GetPortNames()
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to enumerate serial ports.");
            return Array.Empty<string>();
        }
    }
}
