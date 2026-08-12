namespace AirCOM.App.Services;

/// <summary>Which role this AirCOM instance is running as.</summary>
public enum AppRole
{
    /// <summary>Use a remote serial port (opens a com0com virtual port, connects to B-side).</summary>
    ASide,

    /// <summary>Share a local real serial port (listens for A-side connections).</summary>
    BSide,
}
