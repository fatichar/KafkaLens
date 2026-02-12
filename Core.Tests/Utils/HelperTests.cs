using KafkaLens.Core.Utils;
using KafkaLens.Shared.Models;
using Xunit;

namespace KafkaLens.Core.Tests.Utils;

public class HelperTests
{
    [Fact]
    public void CompareTopics_SamePrefix_SortsAlphabetically()
    {
        // Arrange
        var topicA = new Topic("alpha", 1);
        var topicB = new Topic("beta", 1);

        // Act
        var result = Helper.CompareTopics(topicA, topicB);

        // Assert
        Assert.True(result < 0);
    }

    [Fact]
    public void CompareTopics_SamePrefix_ReverseOrder_ReturnsPositive()
    {
        // Arrange
        var topicA = new Topic("beta", 1);
        var topicB = new Topic("alpha", 1);

        // Act
        var result = Helper.CompareTopics(topicA, topicB);

        // Assert
        Assert.True(result > 0);
    }

    [Fact]
    public void CompareTopics_SameName_ReturnsZero()
    {
        // Arrange
        var topicA = new Topic("alpha", 1);
        var topicB = new Topic("alpha", 2);

        // Act
        var result = Helper.CompareTopics(topicA, topicB);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void CompareTopics_FewerUnderscores_ComesFirst()
    {
        // Arrange
        var topicA = new Topic("topic", 1);
        var topicB = new Topic("_topic", 1);

        // Act
        var result = Helper.CompareTopics(topicA, topicB);

        // Assert
        Assert.True(result < 0);
    }

    [Fact]
    public void CompareTopics_MoreUnderscores_ComesLast()
    {
        // Arrange
        var topicA = new Topic("__topic", 1);
        var topicB = new Topic("topic", 1);

        // Act
        var result = Helper.CompareTopics(topicA, topicB);

        // Assert
        Assert.True(result > 0);
    }

    [Fact]
    public void CompareTopics_SameUnderscorePrefix_SortsAlphabetically()
    {
        // Arrange
        var topicA = new Topic("_alpha", 1);
        var topicB = new Topic("_beta", 1);

        // Act
        var result = Helper.CompareTopics(topicA, topicB);

        // Assert
        Assert.True(result < 0);
    }

    [Fact]
    public void CompareTopics_NoUnderscoreVsDoubleUnderscore()
    {
        // Arrange
        var topicA = new Topic("zebra", 1);
        var topicB = new Topic("__alpha", 1);

        // Act
        var result = Helper.CompareTopics(topicA, topicB);

        // Assert
        Assert.True(result < 0);
    }

    [Fact]
    public void CompareTopics_EmptyNames_ReturnsZero()
    {
        // Arrange
        var topicA = new Topic("", 1);
        var topicB = new Topic("", 1);

        // Act
        var result = Helper.CompareTopics(topicA, topicB);

        // Assert
        Assert.Equal(0, result);
    }
}
