using System.Text;
using Confluent.Kafka;
using KafkaLens.Core.Utils;
using Xunit;

namespace KafkaLens.Core.Tests.Utils;

public class MessageConverterTests
{
    [Fact]
    public void CreateMessage_SetsEpochMillisFromTimestamp()
    {
        // Arrange
        var result = CreateConsumeResult(
            timestamp: new Timestamp(1625097600000, TimestampType.CreateTime),
            key: null,
            value: null);

        // Act
        var message = MessageConverter.CreateMessage(result);

        // Assert
        Assert.Equal(1625097600000, message.EpochMillis);
    }

    [Fact]
    public void CreateMessage_SetsPartitionAndOffset()
    {
        // Arrange
        var result = CreateConsumeResult(
            partition: 3,
            offset: 42,
            key: null,
            value: null);

        // Act
        var message = MessageConverter.CreateMessage(result);

        // Assert
        Assert.Equal(3, message.Partition);
        Assert.Equal(42, message.Offset);
    }

    [Fact]
    public void CreateMessage_SetsKeyAndValue()
    {
        // Arrange
        var key = Encoding.UTF8.GetBytes("test-key");
        var value = Encoding.UTF8.GetBytes("test-value");
        var result = CreateConsumeResult(key: key, value: value);

        // Act
        var message = MessageConverter.CreateMessage(result);

        // Assert
        Assert.Equal(key, message.Key);
        Assert.Equal(value, message.Value);
    }

    [Fact]
    public void CreateMessage_NullKeyAndValue_SetsNulls()
    {
        // Arrange
        var result = CreateConsumeResult(key: null, value: null);

        // Act
        var message = MessageConverter.CreateMessage(result);

        // Assert
        Assert.Null(message.Key);
        Assert.Null(message.Value);
    }

    [Fact]
    public void CreateMessage_ConvertsHeaders()
    {
        // Arrange
        var headers = new Headers();
        headers.Add("header1", Encoding.UTF8.GetBytes("value1"));
        headers.Add("header2", Encoding.UTF8.GetBytes("value2"));

        var result = CreateConsumeResult(key: null, value: null, headers: headers);

        // Act
        var message = MessageConverter.CreateMessage(result);

        // Assert
        Assert.Equal(2, message.Headers.Count);
        Assert.Equal("value1", Encoding.UTF8.GetString(message.Headers["header1"]));
        Assert.Equal("value2", Encoding.UTF8.GetString(message.Headers["header2"]));
    }

    [Fact]
    public void CreateMessage_EmptyHeaders_ReturnsEmptyDictionary()
    {
        // Arrange
        var result = CreateConsumeResult(key: null, value: null, headers: new Headers());

        // Act
        var message = MessageConverter.CreateMessage(result);

        // Assert
        Assert.Empty(message.Headers);
    }

    private static ConsumeResult<byte[], byte[]> CreateConsumeResult(
        byte[]? key = null,
        byte[]? value = null,
        int partition = 0,
        long offset = 0,
        Timestamp? timestamp = null,
        Headers? headers = null)
    {
        return new ConsumeResult<byte[], byte[]>
        {
            TopicPartitionOffset = new TopicPartitionOffset("test-topic", partition, offset),
            Message = new Message<byte[], byte[]>
            {
                Key = key!,
                Value = value!,
                Timestamp = timestamp ?? new Timestamp(0, TimestampType.CreateTime),
                Headers = headers ?? new Headers()
            }
        };
    }
}
