using KafkaLens.Formatting;
using Xunit;

namespace KafkaLens.Core.Tests.Formatting;

public class NumericFormatterTests
{
    private readonly Int32Formatter formatter = new();

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
        var int64Formatter = new Int64Formatter();
        byte[] data = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x2A }; // 42 in Big-Endian
        var result = int64Formatter.Format(data, true);
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