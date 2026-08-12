using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AirCOM.Client.A.Com0com;

/// <summary>
/// Status of the com0com virtual serial driver on this machine.
/// </summary>
public enum Com0comState
{
    /// <summary>Service running and the COM10/COM11 (or assigned) pair exists.</summary>
    Ready,

    /// <summary>Service installed but not running.</summary>
    ServiceStopped,

    /// <summary>Service not registered (driver not installed).</summary>
    DriverNotInstalled,

    /// <summary>Service runs but the port pair is missing.</summary>
    PairMissing,
}

/// <summary>
/// Manages com0com driver state on the A-side machine. Checks whether the driver
/// service is installed and running and whether the virtual port pair exists, and
/// can launch an elevated repair script when something is missing.
///
/// Detection (read-only) runs at normal privilege. Repair (pnputil/sc/setupc/
/// bcdedit) requires elevation and is launched via UAC.
/// </summary>
public sealed class Com0comManager
{
    private readonly ILogger<Com0comManager>? _logger;
    private readonly string _setupcPath;
    private readonly string _driverDir;

    public Com0comManager(ILogger<Com0comManager>? logger = null)
    {
        _logger = logger;
        // com0com installs to the 32-bit Program Files even on 64-bit Windows.
        _driverDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "com0com");
        _setupcPath = Path.Combine(_driverDir, "setupc.exe");
    }

    /// <summary>Path to the bundled repair script (copied next to the exe at publish time).</summary>
    public string RepairScriptPath => Path.Combine(AppContext.BaseDirectory, "com0com-repair.ps1");

    public bool DriverDirExists => Directory.Exists(_driverDir);
    public bool SetupcExists => File.Exists(_setupcPath);

    /// <summary>Checks whether the com0com Windows service is registered.</summary>
    public bool IsServiceInstalled()
    {
        try
        {
            // ServiceController requires the service to exist; throws if not.
            using var sc = new ServiceController("com0com");
            _ = sc.Status; // touch to force a check
            return true;
        }
        catch { return false; }
    }

    /// <summary>Checks whether the com0com service is currently running.</summary>
    public bool IsServiceRunning()
    {
        try
        {
            using var sc = new ServiceController("com0com");
            return sc.Status == ServiceControllerStatus.Running;
        }
        catch { return false; }
    }

    /// <summary>Checks whether a given COM port name currently exists on the system.</summary>
    public bool PortExists(string portName)
    {
        try
        {
            var names = System.IO.Ports.SerialPort.GetPortNames();
            return names.Any(n => string.Equals(n, portName, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    /// <summary>Full state check: is com0com ready for the A-side to use a given port pair?</summary>
    public Com0comState CheckState(string expectedPortA, string expectedPortB)
    {
        if (!IsServiceInstalled()) return Com0comState.DriverNotInstalled;
        if (!IsServiceRunning()) return Com0comState.ServiceStopped;
        if (!PortExists(expectedPortA) || !PortExists(expectedPortB)) return Com0comState.PairMissing;
        return Com0comState.Ready;
    }

    /// <summary>
    /// Finds a pair of free COM port names that no existing port is using. Starts
    /// scanning from <paramref name="start"/> upward and returns the first two
    /// consecutive free numbers (e.g. COM50, COM51). Used to avoid colliding with
    /// physical ports or other virtual-port software.
    /// </summary>
    public (string PortA, string PortB) FindFreePortPair(int start = 50)
    {
        var used = new HashSet<string>(
            (System.IO.Ports.SerialPort.GetPortNames() ?? Array.Empty<string>()),
            StringComparer.OrdinalIgnoreCase);

        int a = start;
        while (true)
        {
            string nameA = $"COM{a}";
            string nameB = $"COM{a + 1}";
            if (!used.Contains(nameA) && !used.Contains(nameB))
            {
                return (nameA, nameB);
            }
            a += 2;
            if (a > 200) return (nameA, nameB); // give up scanning, return something
        }
    }

    /// <summary>
    /// Launches the bundled repair PowerShell script elevated (UAC prompt).
    /// The script installs the driver, starts the service, creates the port pair,
    /// and enables test-signing if needed. Returns whether the launch succeeded.
    /// </summary>
    public bool LaunchElevatedRepair(string portA, string portB)
    {
        if (!File.Exists(RepairScriptPath))
        {
            _logger?.LogError("Repair script not found: {Path}", RepairScriptPath);
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                // Pass the desired port names to the script.
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{RepairScriptPath}\" -PortA {portA} -PortB {portB}",
                Verb = "runas",          // triggers UAC
                UseShellExecute = true,  // required for runas
                WindowStyle = ProcessWindowStyle.Normal,
            };
            Process.Start(psi)?.WaitForExit();
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to launch elevated repair.");
            return false;
        }
    }
}
