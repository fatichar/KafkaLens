using Xunit;

namespace KafkaLens.Formatting.Tests;

public class NumericFormattersTests
{
    [Fact]
    public void Int8Formatter_ShouldParseSingleByteSigned()
    {
        var formatter = new Int8Formatter();
        var result = formatter.Format(new byte[] { 0xFF }, prettyPrint: false);
        Assert.Equal("-1", result);
    }

    [Fact]
    public void UInt8Formatter_ShouldParseSingleByteUnsigned()
    {
        var formatter = new UInt8Formatter();
        var result = formatter.Format(new byte[] { 0xFF }, prettyPrint: false);
        Assert.Equal("255", result);
    }

    [Fact]
    public void Int32Formatter_ShouldParseBigEndian()
    {
        var formatter = new Int32Formatter();
        var result = formatter.Format(new byte[] { 0x00, 0x00, 0x00, 0x2A }, prettyPrint: false);
        Assert.Equal("42", result);
    }

    [Fact]
    public void UInt64Formatter_ShouldParseBigEndian()
    {
        var formatter = new UInt64Formatter();
        var result = formatter.Format(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x2A }, prettyPrint: false);
        Assert.Equal("42", result);
    }

    [Fact]
    public void NumericFormatter_ShouldReturnNullForInvalidLength()
    {
        var formatter = new UInt32Formatter();
        var result = formatter.Format(new byte[] { 0x01, 0x02, 0x03 }, prettyPrint: false);
        Assert.Null(result);
    }
}
