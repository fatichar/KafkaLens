using KafkaLens.Formatting;
using Xunit;

namespace Core.Tests.Formatting;

public class NumberFormatterTests
{
    private readonly NumberFormatter formatter = new();

    [Fact]
    public void Format_Int32_BigEndian()
    {
        byte[] data = { 0x00, 0x00, 0x00, 0x2A }; // 42 in Big-Endian
        var result = formatter.Format(data, true);
        Assert.Equal("42", result);
    }

    [Fact]
    public void Format_Int64_BigEndian()
    {
        byte[] data = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x2A }; // 42 in Big-Endian
        var result = formatter.Format(data, true);
        Assert.Equal("42", result);
    }

    [Fact]
    public void Format_InvalidLength()
    {
        byte[] data = { 0x01, 0x02, 0x03 };
        var result = formatter.Format(data, true);
        Assert.Null(result);
    }
}
