using AirCOM.Core.Util;
using FluentAssertions;
using Xunit;

namespace AirCOM.Core.Tests.Protocol;

/// <summary>
/// CRC-16/CCITT-FALSE standard test vectors (from the CRC catalogue, check value 0x29B1).
/// </summary>
public class Crc16Tests
{
    // CRC-16/CCITT-FALSE known check value for the ASCII string "123456789".
    private const ushort ExpectedCheckValue = 0x29B1;

    [Fact]
    public void Compute_KnownCheckValue_Matches()
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes("123456789");

        ushort crc = Crc16.Compute(bytes);

        crc.Should().Be(ExpectedCheckValue);
    }

    [Fact]
    public void Compute_EmptyInput_ReturnsInitialValue()
    {
        // CRC over zero bytes = init value 0xFFFF for CCITT-FALSE.
        ushort crc = Crc16.Compute(Array.Empty<byte>());

        crc.Should().Be(0xFFFF);
    }

    [Fact]
    public void Compute_SameInput_AlwaysSameOutput()
    {
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

        ushort a = Crc16.Compute(data);
        ushort b = Crc16.Compute(data);

        a.Should().Be(b);
    }

    [Fact]
    public void Compute_DiffersWhenBytesDiffer()
    {
        ushort a = Crc16.Compute(new byte[] { 0x01, 0x02, 0x03 });
        ushort b = Crc16.Compute(new byte[] { 0x01, 0x02, 0x04 });

        a.Should().NotBe(b);
    }

    [Fact]
    public void Update_ContinuesRunningCrc()
    {
        var data = System.Text.Encoding.ASCII.GetBytes("123456789");
        ushort full = Crc16.Compute(data);

        // Split the data and compute incrementally.
        ushort partial = Crc16.Compute(data.AsSpan(0, 4));
        partial = Crc16.Update(partial, data.AsSpan(4));

        partial.Should().Be(full);
    }
}
