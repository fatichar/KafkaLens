using System;
using System.Collections.Generic;
using System.IO;
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
        File.WriteAllText(Path.Combine(partitionDir, "msg1.txt"), messageContent);

        var consumer = new SavedMessagesConsumer(testDir);
        consumer.GetTopics();

        var options = new FetchOptions(FetchPosition.START, 100);
        var stream = consumer.GetMessageStream("my-topic", 0, options);

        // Wait briefly for stream to populate
        await Task.Delay(100);

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
        File.WriteAllText(Path.Combine(partitionDir, "msg1.txt"), messageContent);

        var consumer = new SavedMessagesConsumer(testDir);
        consumer.GetTopics();

        var options = new FetchOptions(FetchPosition.START, 100);
        var stream = consumer.GetMessageStream("my-topic", 0, options);

        await Task.Delay(100);

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

        using (var fs = File.Create(Path.Combine(partitionDir, "msg1.klm")))
        {
            original.Serialize(fs);
        }

        var consumer = new SavedMessagesConsumer(testDir);
        consumer.GetTopics();

        var options = new FetchOptions(FetchPosition.START, 100);
        var stream = consumer.GetMessageStream("my-topic", 0, options);

        await Task.Delay(100);

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
            Path.Combine(testDir, "my-topic", "0", "msg1.txt"),
            "Key: k0\n\nvalue0");
        File.WriteAllText(
            Path.Combine(testDir, "my-topic", "1", "msg2.txt"),
            "Key: k1\n\nvalue1");

        var consumer = new SavedMessagesConsumer(testDir);
        consumer.GetTopics();

        var options = new FetchOptions(FetchPosition.START, 100);
        var stream = consumer.GetMessageStream("my-topic", options);

        await Task.Delay(100);

        Assert.Equal(2, stream.Messages.Count);
    }

    #endregion
}
