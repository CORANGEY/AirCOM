using System.IO;
using System.Text;

namespace AirCOM.Core.Util;

/// <summary>
/// File-based diagnostic logger for troubleshooting connection/disconnect issues.
/// Writes timestamped lines to %Temp%\aircom-diag.txt. OFF by default; set the
/// AIRCOM_DIAG environment variable to 1 to enable. Thread-safe.
///
/// Kept in the codebase (disabled) so we can re-enable it quickly when a user
/// reports a connection issue, without rebuilding.
/// </summary>
public static class DiagLog
{
    private static readonly object _gate = new();
    private static readonly string _path = Path.Combine(Path.GetTempPath(), "aircom-diag.txt");
    private static readonly bool _enabled =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AIRCOM_DIAG"));

    public static void Log(string message)
    {
        if (!_enabled) return;
        try
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} [tid={Environment.CurrentManagedThreadId}] {message}\n";
            lock (_gate) { File.AppendAllText(_path, line, Encoding.UTF8); }
        }
        catch { }
    }

    public static string PathString => _path;
}
