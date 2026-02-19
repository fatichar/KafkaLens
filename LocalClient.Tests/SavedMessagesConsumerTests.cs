using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KafkaLens.Shared.Models;
using Xunit;

namespace KafkaLens.Clients;

public class SavedMessagesConsumerTests : IDisposable
{
    private readonly string testDir;

    public SavedMessagesConsumerTests()
    {
        testDir = Path.Combine(Path.GetTempPath(), "KafkaLensTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(testDir))
        {
            Directory.Delete(testDir, true);
        }
    }
    
    #region Helpers

    private void CreateTextMessage(string topic, int partition, long offset, long timestamp, string key, string value)
    {
        var partitionDir = Path.Combine(testDir, topic, partition.ToString());
        Directory.CreateDirectory(partitionDir);

        var dto = DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
        var messageContent = $"Key: {key}\nTimestamp: {dto.ToString("o")}\nOffset: {offset}\n\n{value}";
        File.WriteAllText(Path.Combine(partitionDir, $"{offset}.txt"), messageContent);
    }

    private void CreateBinaryMessage(string topic, int partition, long offset, long timestamp, string key, string value)
    {
        var partitionDir = Path.Combine(testDir, topic, partition.ToString());
        Directory.CreateDirectory(partitionDir);

        var message = new Message(
            timestamp,
            new Dictionary<string, byte[]>(),
            System.Text.Encoding.UTF8.GetBytes(key),
            System.Text.Encoding.UTF8.GetBytes(value))
        {
            Partition = partition,
            Offset = offset
        };

        using var fs = File.Create(Path.Combine(partitionDir, $"{offset}.klm"));
        message.Serialize(fs);
    }
    
    #endregion

    #region ValidateConnection

    [Fact]
    public void ValidateConnection_ExistingDirectory_ReturnsTrue()
    {
        var consumer = new SavedMessagesConsumer(testDir);

        Assert.True(consumer.ValidateConnection());
    }

    [Fact]
    public void ValidateConnection_NonExistingDirectory_ReturnsFalse()
    {
        var consumer = new SavedMessagesConsumer(Path.Combine(testDir, "nonexistent"));

        Assert.False(consumer.ValidateConnection());
    }

    #endregion

    #region GetTopics

    [Fact]
    public void GetTopics_EmptyDirectory_ReturnsEmpty()
    {
        var consumer = new SavedMessagesConsumer(testDir);

        var topics = consumer.GetTopics();

        Assert.Empty(topics);
    }

    [Fact]
    public void GetTopics_WithTopicDirectories_ReturnsTopics()
    {
        var topicDir = Path.Combine(testDir, "my-topic");
        Directory.CreateDirectory(Path.Combine(topicDir, "0"));
        Directory.CreateDirectory(Path.Combine(topicDir, "1"));

        var consumer = new SavedMessagesConsumer(testDir);

        var topics = consumer.GetTopics();

        Assert.Single(topics);
        Assert.Equal("my-topic", topics[0].Name);
        Assert.Equal(2, topics[0].PartitionCount);
    }

    [Fact]
    public void GetTopics_MultipleTopics_ReturnsAll()
    {
        Directory.CreateDirectory(Path.Combine(testDir, "topic-a", "0"));
        Directory.CreateDirectory(Path.Combine(testDir, "topic-b", "0"));
        Directory.CreateDirectory(Path.Combine(testDir, "topic-b", "1"));

        var consumer = new SavedMessagesConsumer(testDir);

        var topics = consumer.GetTopics();

        Assert.Equal(2, topics.Count);
    }

    [Fact]
    public void GetTopics_CalledTwice_RefreshesTopics()
    {
        Directory.CreateDirectory(Path.Combine(testDir, "topic-a", "0"));

        var consumer = new SavedMessagesConsumer(testDir);

        var topics1 = consumer.GetTopics();
        Assert.Single(topics1);

        Directory.CreateDirectory(Path.Combine(testDir, "topic-b", "0"));

        var topics2 = consumer.GetTopics();
        Assert.Equal(2, topics2.Count);
    }

    #endregion

    #region GetMessageStream - text files

    [Fact]
    public async Task GetMessageStream_TextFile_ReadsMessage()
    {
        var partitionDir = Path.Combine(testDir, "my-topic", "0");
        Directory.CreateDirectory(partitionDir);

        var messageContent = "Key: test-key\nTimestamp: 2024-01-01T00:00:00\n\nHello World";
        File.WriteAllText(Path.Combine(partitionDir, "10.txt"), messageContent);

        var consumer = new SavedMessagesConsumer(testDir);
        consumer.GetTopics();

        var options = new FetchOptions(FetchPosition.START, 100);
        var stream = consumer.GetMessageStream("my-topic", 0, options);

        // Wait briefly for stream to populate
        await Task.Delay(500);

        Assert.NotEmpty(stream.Messages);
        Assert.Equal("test-key", stream.Messages[0].KeyText);
        Assert.Equal(0, stream.Messages[0].Partition);
    }

    [Fact]
    public async Task GetMessageStream_TextFileWithHeaders_ParsesHeaders()
    {
        var partitionDir = Path.Combine(testDir, "my-topic", "0");
        Directory.CreateDirectory(partitionDir);

        var messageContent = "Key: key1\nTimestamp: 2024-01-01T00:00:00\nHeaders:\n  Content-Type: application/json\n\n{\"data\":1}";
        File.WriteAllText(Path.Combine(partitionDir, "10.txt"), messageContent);

        var consumer = new SavedMessagesConsumer(testDir);
        consumer.GetTopics();

        var options = new FetchOptions(FetchPosition.START, 100);
        var stream = consumer.GetMessageStream("my-topic", 0, options);

        await Task.Delay(500);

        Assert.NotEmpty(stream.Messages);
        Assert.True(stream.Messages[0].Headers.ContainsKey("Content-Type"));
    }

    [Fact]
    public async Task GetMessageStream_KlmFile_DeserializesMessage()
    {
        var partitionDir = Path.Combine(testDir, "my-topic", "0");
        Directory.CreateDirectory(partitionDir);

        var original = new Message(
            1704067200000,
            new Dictionary<string, byte[]>(),
            System.Text.Encoding.UTF8.GetBytes("key1"),
            System.Text.Encoding.UTF8.GetBytes("value1"))
        {
            Partition = 0,
            Offset = 42
        };

        using (var fs = File.Create(Path.Combine(partitionDir, "42.klm")))
        {
            original.Serialize(fs);
        }

        var consumer = new SavedMessagesConsumer(testDir);
        consumer.GetTopics();

        var options = new FetchOptions(FetchPosition.START, 100);
        var stream = consumer.GetMessageStream("my-topic", 0, options);

        await Task.Delay(500);

        Assert.NotEmpty(stream.Messages);
        Assert.Equal(42, stream.Messages[0].Offset);
        Assert.Equal("key1", stream.Messages[0].KeyText);
    }

    [Fact]
    public async Task GetMessageStream_TopicLevel_ReadsAllPartitions()
    {
        Directory.CreateDirectory(Path.Combine(testDir, "my-topic", "0"));
        Directory.CreateDirectory(Path.Combine(testDir, "my-topic", "1"));

        File.WriteAllText(
            Path.Combine(testDir, "my-topic", "0", "10.txt"),
            "Key: k0\n\nvalue0");
        File.WriteAllText(
            Path.Combine(testDir, "my-topic", "1", "20.txt"),
            "Key: k1\n\nvalue1");

        var consumer = new SavedMessagesConsumer(testDir);
        consumer.GetTopics();

        var options = new FetchOptions(FetchPosition.START, 100);
        var stream = consumer.GetMessageStream("my-topic", options);

        await Task.Delay(500);

        Assert.Equal(2, stream.Messages.Count);
    }

    #endregion
    
    #region GetMessageStream - With Options

    [Fact]
    public async Task GetMessageStream_Partition_RespectsLimit()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            CreateTextMessage("my-topic", 0, i, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), $"k{i}", $"v{i}");
        }
        var consumer = new SavedMessagesConsumer(testDir);
        var options = new FetchOptions(FetchPosition.START, 5);

        // Act
        var stream = consumer.GetMessageStream("my-topic", 0, options);
        await Task.Delay(500);

        // Assert
        Assert.Equal(5, stream.Messages.Count);
    }

    [Fact]
    public async Task GetMessageStream_Partition_RespectsStartOffset()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            CreateTextMessage("my-topic", 0, i, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), $"k{i}", $"v{i}");
        }
        var consumer = new SavedMessagesConsumer(testDir);
        var options = new FetchOptions(new(PositionType.OFFSET, 5), 10);

        // Act
        var stream = consumer.GetMessageStream("my-topic", 0, options);
        await Task.Delay(500);

        // Assert
            Assert.Equal(5, stream.Messages.Count);
            var sorted = stream.Messages.OrderBy(m => m.Offset).ToList();
            Assert.Equal(5, sorted[0].Offset);    }

    [Fact]
    public async Task GetMessageStream_Partition_RespectsEndOffset()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            CreateBinaryMessage("my-topic", 0, i, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), $"k{i}", $"v{i}");
        }
        var consumer = new SavedMessagesConsumer(testDir);
        var options = new FetchOptions(start: FetchPosition.START, end: new(PositionType.OFFSET, 5));
        options.Limit = 10;

        // Act
        var stream = consumer.GetMessageStream("my-topic", 0, options);
        await Task.Delay(500);

        // Assert
        Assert.Equal(6, stream.Messages.Count); // 0 to 5 inclusive
        var sorted = stream.Messages.OrderBy(m => m.Offset).ToList();
        Assert.Equal(0, sorted[0].Offset);
        Assert.Equal(5, sorted.Last().Offset);
    }

    [Fact]
    public async Task GetMessageStream_Partition_RespectsTimestamp()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 10; i++)
        {
            CreateTextMessage("my-topic", 0, i, now.AddSeconds(i).ToUnixTimeMilliseconds(), $"k{i}", $"v{i}");
        }
        var consumer = new SavedMessagesConsumer(testDir);
        var startTs = now.AddSeconds(5).ToUnixTimeMilliseconds();
        var options = new FetchOptions(new(PositionType.TIMESTAMP, startTs), 10);

        // Act
        var stream = consumer.GetMessageStream("my-topic", 0, options);
        await Task.Delay(500);

        // Assert
        Assert.Equal(5, stream.Messages.Count);
        Assert.True(stream.Messages[0].EpochMillis >= startTs);
    }
    
    [Fact]
    public async Task GetMessageStream_Topic_RespectsLimit()
    {
        // Arrange
        // 10 messages in partition 0, 10 in partition 1
        for (int i = 0; i < 10; i++)
        {
            CreateTextMessage("my-topic", 0, i, DateTimeOffset.UtcNow.AddSeconds(i*2).ToUnixTimeMilliseconds(), $"k{i}", $"v{i}");
            CreateTextMessage("my-topic", 1, i, DateTimeOffset.UtcNow.AddSeconds(i*2+1).ToUnixTimeMilliseconds(), $"k{i+10}", $"v{i+10}");
        }
        var consumer = new SavedMessagesConsumer(testDir);
        var options = new FetchOptions(FetchPosition.START, 5);

        // Act
        var stream = consumer.GetMessageStream("my-topic", options);
        await Task.Delay(500); // give more time for topic fetch

        // Assert
        Assert.Equal(5, stream.Messages.Count);
    }

    [Fact]
    public async Task GetMessageStream_Topic_ReturnsMessagesInTimestampOrder()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        CreateTextMessage("my-topic", 0, 0, now.AddSeconds(2).ToUnixTimeMilliseconds(), "k0", "v0");
        CreateTextMessage("my-topic", 1, 0, now.AddSeconds(1).ToUnixTimeMilliseconds(), "k1", "v1");
        CreateTextMessage("my-topic", 0, 1, now.AddSeconds(4).ToUnixTimeMilliseconds(), "k2", "v2");
        CreateTextMessage("my-topic", 1, 1, now.AddSeconds(3).ToUnixTimeMilliseconds(), "k3", "v3");
        
        var consumer = new SavedMessagesConsumer(testDir);
        var options = new FetchOptions(FetchPosition.START, 10);

        // Act
        var stream = consumer.GetMessageStream("my-topic", options);
        await Task.Delay(500);

        // Assert
        Assert.Equal(4, stream.Messages.Count);
        Assert.Equal("k1", stream.Messages[0].KeyText);
        Assert.Equal("k0", stream.Messages[1].KeyText);
        Assert.Equal("k3", stream.Messages[2].KeyText);
        Assert.Equal("k2", stream.Messages[3].KeyText);
    }

    [Fact]
    public async Task GetMessageStream_Topic_FromEnd_ReturnsLatestMessages()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 10; i++)
        {
            CreateTextMessage("my-topic", 0, i, now.AddSeconds(i).ToUnixTimeMilliseconds(), $"k{i}", $"v{i}");
        }
        
        var consumer = new SavedMessagesConsumer(testDir);
        var options = new FetchOptions(start: new FetchPosition(PositionType.OFFSET, -1 - 3), end: new FetchPosition(PositionType.OFFSET, -1))
        {
            Limit = 3
        };

        // Act
        var stream = consumer.GetMessageStream("my-topic", options);
        await Task.Delay(500);

        // Assert
        Assert.Equal(3, stream.Messages.Count);
        // We expect chronological order [k7, k8, k9] as we stream messages
        Assert.Equal("k7", stream.Messages[0].KeyText);
        Assert.Equal("k8", stream.Messages[1].KeyText);
        Assert.Equal("k9", stream.Messages[2].KeyText); // Latest message
    }

    #endregion
}