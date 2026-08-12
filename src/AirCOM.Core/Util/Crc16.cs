namespace AirCOM.Core.Util;

/// <summary>
/// CRC-16/CCITT-FALSE (polynomial 0x1021, init 0xFFFF, no reflection, no xor-out).
/// Standard checksum used for frame integrity verification.
/// </summary>
public static class Crc16
{
    private const ushort Polynomial = 0x1021;
    private const ushort InitialValue = 0xFFFF;

    /// <summary>Lookup table built from the CCITT-FALSE polynomial.</summary>
    private static readonly ushort[] Table = BuildTable();

    private static ushort[] BuildTable()
    {
        var table = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            ushort crc = (ushort)(i << 8);
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000) != 0
                    ? (ushort)((crc << 1) ^ Polynomial)
                    : (ushort)(crc << 1);
            }
            table[i] = crc;
        }
        return table;
    }

    /// <summary>Compute CRC-16/CCITT-FALSE over the given bytes.</summary>
    public static ushort Compute(ReadOnlySpan<byte> bytes)
    {
        ushort crc = InitialValue;
        foreach (byte b in bytes)
        {
            crc = (ushort)((crc << 8) ^ Table[((crc >> 8) ^ b) & 0xFF]);
        }
        return crc;
    }

    /// <summary>Continue a running CRC with more bytes.</summary>
    public static ushort Update(ushort crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            crc = (ushort)((crc << 8) ^ Table[((crc >> 8) ^ b) & 0xFF]);
        }
        return crc;
    }
}
