using KafkaLens.Shared.Models;
using Xunit;

namespace KafkaLens.Core.Tests.Models;

public class FetchPositionTests
{
    [Fact]
    public void Constructor_WithOffset_SetsPropertiesCorrectly()
    {
        // Arrange
        var type = PositionType.Offset;
        long value = 12345;

        // Act
        var fetchPosition = new FetchPosition(type, value);

        // Assert
        Assert.Equal(type, fetchPosition.Type);
        Assert.Equal(value, fetchPosition.Offset);
        Assert.Equal(0, fetchPosition.Timestamp);
    }

    [Fact]
    public void Constructor_WithTimestamp_SetsPropertiesCorrectly()
    {
        // Arrange
        var type = PositionType.Timestamp;
        long value = 1625097600000;

        // Act
        var fetchPosition = new FetchPosition(type, value);

        // Assert
        Assert.Equal(type, fetchPosition.Type);
        Assert.Equal(value, fetchPosition.Timestamp);
        Assert.Equal(0, fetchPosition.Offset);
    }

    [Fact]
    public void Start_IsOffsetZero()
    {
        // Act
        var fetchPosition = FetchPosition.Start;

        // Assert
        Assert.Equal(PositionType.Offset, fetchPosition.Type);
        Assert.Equal(0, fetchPosition.Offset);
    }

    [Fact]
    public void End_IsOffsetMinusOne()
    {
        // Act
        var fetchPosition = FetchPosition.End;

        // Assert
        Assert.Equal(PositionType.Offset, fetchPosition.Type);
        Assert.Equal(-1, fetchPosition.Offset);
    }

    [Fact]
    public void SetOffset_UpdatesProperties()
    {
        // Arrange
        var fetchPosition = new FetchPosition(PositionType.Timestamp, 12345);

        // Act
        fetchPosition.SetOffset(67890);

        // Assert
        Assert.Equal(PositionType.Offset, fetchPosition.Type);
        Assert.Equal(67890, fetchPosition.Offset);
    }
}