using System.Runtime.InteropServices;

namespace AirCOM.Core.Util;

/// <summary>
/// P/Invoke declarations for the Win32 communication functions used to drive
/// serial control lines (DTR/RTS/DCD/CTS/DSR/RI) beyond what System.IO.Ports exposes.
/// </summary>
internal static class Win32Api
{
    // --- EscapeCommFunction dwFunc values ---
    public const uint SETXOFF = 1;
    public const uint SETXON = 2;
    public const uint SETRTS = 3;
    public const uint CLRRTS = 4;
    public const uint SETDTR = 5;
    public const uint CLRDTR = 6;
    public const uint SETBREAK = 8;
    public const uint CLRBREAK = 9;

    // --- GetCommModemStatus bit masks ---
    public const uint MS_CTS_ON = 0x0010;   // Clear-to-send
    public const uint MS_DSR_ON = 0x0020;   // Data-set-ready
    public const uint MS_RING_ON = 0x0040;  // Ring indicator
    public const uint MS_RLSD_ON = 0x0080;  // Receive-line-signal-detect (DCD/CD)

    // --- SetCommMask event flags ---
    public const uint EV_CTS = 0x0004;
    public const uint EV_DSR = 0x0008;
    public const uint EV_RLSD = 0x0020;
    public const uint EV_RING = 0x0100;
    public const uint EV_ERR = 0x0080;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    /// <summary>Directs a communications device to perform an extended function (set/clear DTR/RTS, break).</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EscapeCommFunction(IntPtr hFile, uint dwFunc);

    /// <summary>Retrieves the modem control-register values (CTS/DSR/RING/DCD).</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCommModemStatus(IntPtr hFile, out uint lpModemStat);

    /// <summary>Specifies a set of events to be monitored for a communications device.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCommMask(IntPtr hFile, uint dwEvtMask);

    /// <summary>Blocks until one of the masked events occurs.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WaitCommEvent(IntPtr hFile, out uint lpEvtMask, IntPtr lpOverlapped);

    /// <summary>Configures a communications device per DCB structure.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCommState(IntPtr hFile, ref DCB lpDCB);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCommState(IntPtr hFile, ref DCB lpDCB);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCommTimeouts(IntPtr hFile, ref COMMTIMEOUTS lpCommTimeouts);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupComm(IntPtr hFile, uint dwInQueue, uint dwOutQueue);

    /// <summary>Device Control Block, used to set baud/parity/stop bits directly.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DCB
    {
        public uint DCBlength;
        public uint BaudRate;
        public uint Flags;        // bitfield: fBinary, fParity, fOutxCtsFlow, fDtrControl, ...
        public ushort wReserved;
        public ushort XonLim;
        public ushort XoffLim;
        public byte ByteSize;     // data bits 4-8
        public byte Parity;       // 0=none,1=odd,2=even,3=mark,4=space
        public byte StopBits;     // 0=1,1=1.5,2=2
        public byte XonChar;
        public byte XoffChar;
        public byte ErrorChar;
        public byte EofChar;
        public byte EvtChar;
        public ushort wReserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct COMMTIMEOUTS
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }
}
