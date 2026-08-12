using System.IO;
using System.Text.Json;

namespace AirCOM.App.Services;

/// <summary>
/// Persistent settings for the AirCOM app, stored as JSON in the user's
/// <c>%AppData%\AirCOM</c> directory. This is shared across all versions
/// (so the assigned com0com port pair stays stable regardless of which
/// extracted folder the exe is launched from).
/// </summary>
public sealed class AppSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AirCOM");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "aircom-settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    /// <summary>The COM port the user's serial app opens (e.g. COM50).</summary>
    public string? UserPort { get; set; }

    /// <summary>The COM port AirCOM drives (e.g. COM51).</summary>
    public string? ServicePort { get; set; }

    /// <summary>Last used remote host (A-side) or empty.</summary>
    public string? LastRemoteHost { get; set; }

    /// <summary>Last used remote/listen port.</summary>
    public int LastTcpPort { get; set; } = 51000;

    /// <summary>Last used baud rate.</summary>
    public int LastBaudRate { get; set; } = 115200;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch { }
    }

    /// <summary>Path to the settings file (for diagnostics / reset).</summary>
    public static string GetSettingsPath() => SettingsPath;
}
