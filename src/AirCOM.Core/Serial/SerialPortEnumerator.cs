using System.IO.Ports;
using System.Management;
using Microsoft.Extensions.Logging;

namespace AirCOM.Core.Serial;

/// <summary>
/// A serial port with its friendly device name (from WMI), e.g. port "COM4",
/// description "USB-SERIAL CH340 (COM4)".
/// </summary>
public sealed record SerialPortInfo(string PortName, string Description)
{
    /// <summary>Display string for UI dropdowns: "COM4 - USB-SERIAL CH340".</summary>
    public string Display => Description.Contains($"({PortName})")
        ? $"{PortName} - {Description.Replace($"({PortName})", "").Trim()}"
        : $"{PortName} - {Description}";
}

/// <summary>
/// Enumerates local serial ports available on the machine, with optional device
/// descriptions from WMI (Win32_PnPEntity).
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

    /// <summary>
    /// Returns COM ports with device descriptions (e.g. "COM4", "USB-SERIAL CH340 (COM4)").
    /// Falls back to just port names if WMI is unavailable.
    /// </summary>
    public static IReadOnlyList<SerialPortInfo> GetPortsWithDescription(ILogger? logger = null)
    {
        var names = GetPortNames(logger);
        var result = new List<SerialPortInfo>(names.Count);
        try
        {
            // WMI query: all PnP entities whose name contains a (COMx) reference.
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            var descMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mo in searcher.Get())
            {
                var name = mo["Name"] as string;
                if (string.IsNullOrEmpty(name)) continue;
                // Extract "(COMn)" from the name, e.g. "USB-SERIAL CH340 (COM4)".
                var open = name.LastIndexOf('(');
                var close = name.LastIndexOf(')');
                if (open < 0 || close <= open) continue;
                var port = name[(open + 1)..close];
                if (port.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                    descMap[port] = name[..open].Trim();
            }

            foreach (var n in names)
            {
                result.Add(descMap.TryGetValue(n, out var desc)
                    ? new SerialPortInfo(n, desc)
                    : new SerialPortInfo(n, n));
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "WMI port description query failed; falling back to names only.");
            foreach (var n in names) result.Add(new SerialPortInfo(n, n));
        }
        return result;
    }
}
