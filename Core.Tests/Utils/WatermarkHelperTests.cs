using Confluent.Kafka;
using KafkaLens.Core.Utils;
using KafkaLens.Shared.Models;
using Xunit;

namespace KafkaLens.Core.Tests.Utils;

public class WatermarkHelperTests
{
    [Fact]
    public void UpdateForWatermarks_PositiveOffset_WithinRange_KeepsOffset()
    {
        // Arrange
        var options = new FetchOptions(new FetchPosition(PositionType.OFFSET, 50), 10);
        var watermarks = new WatermarkOffsets(new Offset(0), new Offset(100));

        // Act
        WatermarkHelper.UpdateForWatermarks(options, watermarks);

        // Assert
        Assert.Equal(50, options.Start.Offset);
        Assert.Equal(10, options.Limit);
    }

    [Fact]
    public void UpdateForWatermarks_NegativeOffset_CalculatesFromHigh()
    {
        // Arrange: offset = -1 => high + 1 + (-1) = 100
        var options = new FetchOptions(new FetchPosition(PositionType.OFFSET, -1), 10);
        var watermarks = new WatermarkOffsets(new Offset(0), new Offset(100));

        // Act
        WatermarkHelper.UpdateForWatermarks(options, watermarks);

        // Assert
        Assert.Equal(100, options.Start.Offset);
        Assert.Equal(0, options.Limit);
    }

    [Fact]
    public void UpdateForWatermarks_NegativeOffset_LargeNegative_ClampsToLow()
    {
        // Arrange: offset = -200 => high + 1 + (-200) = 100 + 1 - 200 = -99 => clamped to low (10)
        var options = new FetchOptions(new FetchPosition(PositionType.OFFSET, -200), 10);
        var watermarks = new WatermarkOffsets(new Offset(10), new Offset(100));

        // Act
        WatermarkHelper.UpdateForWatermarks(options, watermarks);

        // Assert
        Assert.Equal(10, options.Start.Offset);
    }

    [Fact]
    public void UpdateForWatermarks_OffsetBelowLow_ClampsToLow()
    {
        // Arrange
        var options = new FetchOptions(new FetchPosition(PositionType.OFFSET, 5), 10);
        var watermarks = new WatermarkOffsets(new Offset(20), new Offset(100));

        // Act
        WatermarkHelper.UpdateForWatermarks(options, watermarks);

        // Assert
        Assert.Equal(20, options.Start.Offset);
    }

    [Fact]
    public void UpdateForWatermarks_OffsetAboveHigh_ClampsToHigh()
    {
        // Arrange
        var options = new FetchOptions(new FetchPosition(PositionType.OFFSET, 150), 10);
        var watermarks = new WatermarkOffsets(new Offset(0), new Offset(100));

        // Act
        WatermarkHelper.UpdateForWatermarks(options, watermarks);

        // Assert
        Assert.Equal(100, options.Start.Offset);
        Assert.Equal(0, options.Limit);
    }

    [Fact]
    public void UpdateForWatermarks_LimitExceedsHigh_ClampsLimit()
    {
        // Arrange: offset=90, limit=20 => 90+20=110 > 100 => limit = 100-90 = 10
        var options = new FetchOptions(new FetchPosition(PositionType.OFFSET, 90), 20);
        var watermarks = new WatermarkOffsets(new Offset(0), new Offset(100));

        // Act
        WatermarkHelper.UpdateForWatermarks(options, watermarks);

        // Assert
        Assert.Equal(90, options.Start.Offset);
        Assert.Equal(10, options.Limit);
    }

    [Fact]
    public void UpdateForWatermarks_LimitWithinRange_KeepsLimit()
    {
        // Arrange: offset=50, limit=10 => 50+10=60 <= 100 => limit stays 10
        var options = new FetchOptions(new FetchPosition(PositionType.OFFSET, 50), 10);
        var watermarks = new WatermarkOffsets(new Offset(0), new Offset(100));

        // Act
        WatermarkHelper.UpdateForWatermarks(options, watermarks);

        // Assert
        Assert.Equal(10, options.Limit);
    }

    [Fact]
    public void UpdateForWatermarks_OffsetAtLow_KeepsOffset()
    {
        // Arrange
        var options = new FetchOptions(new FetchPosition(PositionType.OFFSET, 20), 5);
        var watermarks = new WatermarkOffsets(new Offset(20), new Offset(100));

        // Act
        WatermarkHelper.UpdateForWatermarks(options, watermarks);

        // Assert
        Assert.Equal(20, options.Start.Offset);
        Assert.Equal(5, options.Limit);
    }

    [Fact]
    public void UpdateForWatermarks_OffsetAtHigh_SetsLimitToZero()
    {
        // Arrange: offset=100 (at high), limit=10 => 100+10=110 > 100 => limit = 100-100 = 0
        var options = new FetchOptions(new FetchPosition(PositionType.OFFSET, 100), 10);
        var watermarks = new WatermarkOffsets(new Offset(0), new Offset(100));

        // Act
        WatermarkHelper.UpdateForWatermarks(options, watermarks);

        // Assert
        Assert.Equal(100, options.Start.Offset);
        Assert.Equal(0, options.Limit);
    }

    [Fact]
    public void UpdateForWatermarks_EmptyPartition_SetsLimitToZero()
    {
        // Arrange: low == high means empty partition
        var options = new FetchOptions(new FetchPosition(PositionType.OFFSET, 0), 10);
        var watermarks = new WatermarkOffsets(new Offset(0), new Offset(0));

        // Act
        WatermarkHelper.UpdateForWatermarks(options, watermarks);

        // Assert
        Assert.Equal(0, options.Start.Offset);
        Assert.Equal(0, options.Limit);
    }
}
