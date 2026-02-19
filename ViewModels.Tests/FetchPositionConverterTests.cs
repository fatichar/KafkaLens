namespace KafkaLens.ViewModels.Tests;

using System.Globalization;

public class FetchPositionConverterTests
{
    private readonly FetchPositionConverter _converter = new();

    [Fact]
    public void Convert_PositionNull_ReturnsFalse()
    {
        var result = _converter.Convert(null, typeof(bool), null, CultureInfo.InvariantCulture);
        result.Should().Be(false);
    }

    [Fact]
    public void Convert_PositionTimestamp_ParameterNull_ReturnsTrue()
    {
        var result = _converter.Convert("Timestamp", typeof(bool), null, CultureInfo.InvariantCulture);
        result.Should().Be(true);
    }

    [Fact]
    public void Convert_PositionOffset_ParameterNull_ReturnsFalse()
    {
        var result = _converter.Convert("Offset", typeof(bool), null, CultureInfo.InvariantCulture);
        result.Should().Be(false);
    }

    [Theory]
    [InlineData("Offset", "Offset", true)]
    [InlineData("Offset", "offset", true)]
    [InlineData("Offset", "Timestamp", false)]
    [InlineData("Timestamp", "Timestamp", true)]
    [InlineData("Timestamp", "Offset", false)]
    public void Convert_WithParameter_ReturnsExpected(string position, string parameter, bool expected)
    {
        var result = _converter.Convert(position, typeof(bool), parameter, CultureInfo.InvariantCulture);
        result.Should().Be(expected);
    }

    private class ThrowingObject
    {
        public override string ToString() => throw new Exception("Test exception");
    }

    [Fact]
    public void Convert_ToStringThrows_ReturnsFalse()
    {
        var result = _converter.Convert(new ThrowingObject(), typeof(bool), null, CultureInfo.InvariantCulture);
        result.Should().Be(false);
    }

    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        Action act = () => _converter.ConvertBack(true, typeof(string), null, CultureInfo.InvariantCulture);
        act.Should().Throw<NotImplementedException>();
    }
}
