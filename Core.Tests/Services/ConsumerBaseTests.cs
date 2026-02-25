using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KafkaLens.Core.Services;
using KafkaLens.Shared.Models;
using Xunit;

namespace KafkaLens.Core.Tests.Services;

public class ConsumerBaseTests
{
    private class TestConsumer(List<Topic> topics, bool validateResult = true, List<Message>? messages = null)
        : ConsumerBase
    {
        public override bool ValidateConnection() => validateResult;

        protected override List<Topic> FetchTopics() => topics;

        protected override Task GetMessagesAsync(string topicName, FetchOptions options, MessageStream messages1, CancellationToken cancellationToken)
        {
            if (messages != null)
            {
                foreach (var msg in messages)
                {
                    messages1.Messages.Add(msg);
                }
            }
            messages1.HasMore = false;
            return Task.CompletedTask;
        }

        protected override Task GetMessagesAsync(string topicName, int partition, FetchOptions options, MessageStream messages1, CancellationToken cancellationToken)
        {
            if (messages != null)
            {
                foreach (var msg in messages)
                {
                    messages1.Messages.Add(msg);
                }
            }
            messages1.HasMore = false;
            return Task.CompletedTask;
        }

        public void SetLastRefreshTime(DateTime time)
        {
            LastRefreshTime = time;
        }

        public Dictionary<string, Topic> GetTopicsDict() => Topics;
    }

    #region GetTopics

    [Fact]
    public void GetTopics_FirstCall_LoadsAndReturnsTopics()
    {
        // Arrange
        var topics = new List<Topic>
        {
            new Topic("topic1", 1),
            new Topic("topic2", 2)
        };
        var consumer = new TestConsumer(topics);

        // Act
        var result = consumer.GetTopics();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, t => t.Name == "topic1");
        Assert.Contains(result, t => t.Name == "topic2");
    }

    [Fact]
    public void GetTopics_CalledTwiceWithinRefreshInterval_ReturnsCachedTopics()
    {
        // Arrange
        var topics = new List<Topic> { new Topic("topic1", 1) };
        var consumer = new TestConsumer(topics);

        // Act
        var result1 = consumer.GetTopics();
        var result2 = consumer.GetTopics();

        // Assert
        Assert.Equal(result1.Count, result2.Count);
        Assert.Equal("topic1", result2[0].Name);
    }

    [Fact]
    public void GetTopics_AfterRefreshIntervalExpires_ReloadsTopics()
    {
        // Arrange
        var topics = new List<Topic> { new Topic("topic1", 1) };
        var consumer = new TestConsumer(topics);

        // Load topics initially
        consumer.GetTopics();

        // Simulate expired refresh interval
        consumer.SetLastRefreshTime(DateTime.Now - TimeSpan.FromMinutes(61));

        // Act
        var result = consumer.GetTopics();

        // Assert
        Assert.Single(result);
        Assert.Equal("topic1", result[0].Name);
    }

    [Fact]
    public void GetTopics_FetchThrows_WrapsException()
    {
        // Arrange
        var consumer = new ThrowingConsumer();

        // Act & Assert
        var ex = Assert.Throws<Exception>(() => consumer.GetTopics());
        Assert.Equal("Failed to load topics", ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void GetTopics_WhenFetchReturnsDuplicateTopicNames_FiltersDuplicates()
    {
        // Arrange
        var topics = new List<Topic>
        {
            new Topic("topic1", 1),
            new Topic("topic1", 2),
            new Topic("topic2", 1)
        };
        var consumer = new TestConsumer(topics);

        // Act
        var result = consumer.GetTopics();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result.Count(t => t.Name == "topic1"));
        Assert.Equal(1, result.Count(t => t.Name == "topic2"));
    }

    [Fact]
    public async Task GetTopics_CalledConcurrently_LoadsOnlyOnce()
    {
        // Arrange
        var consumer = new ConcurrentLoadConsumer(new List<Topic> { new Topic("topic1", 1) });

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => consumer.GetTopics())));

        // Assert
        Assert.All(results, topics => Assert.Single(topics));
        Assert.Equal(1, consumer.FetchCount);
    }

    #endregion GetTopics

    #region ValidateTopic

    [Fact]
    public void GetMessageStream_TopicOnly_ReturnsMessageStream()
    {
        // Arrange
        var topics = new List<Topic> { new Topic("topic1", 1) };
        var messages = new List<Message>
        {
            new Message(1000, new Dictionary<string, byte[]>(), null, null)
        };
        var consumer = new TestConsumer(topics, messages: messages);
        consumer.GetTopics(); // pre-load topics

        var options = new FetchOptions(FetchPosition.Start, 10);

        // Act
        var stream = consumer.GetMessageStream("topic1", options);

        // Assert
        Assert.NotNull(stream);
    }

    [Fact]
    public void GetMessageStream_WithPartition_ReturnsMessageStream()
    {
        // Arrange
        var topics = new List<Topic> { new Topic("topic1", 2) };
        var consumer = new TestConsumer(topics);
        consumer.GetTopics();

        var options = new FetchOptions(FetchPosition.Start, 10);

        // Act
        var stream = consumer.GetMessageStream("topic1", 0, options);

        // Assert
        Assert.NotNull(stream);
    }

    [Fact]
    public async Task GetMessagesAsync_TopicOnly_ReturnsMessages()
    {
        // Arrange
        var topics = new List<Topic> { new Topic("topic1", 1) };
        var messages = new List<Message>
        {
            new Message(1000, new Dictionary<string, byte[]>(), null, null),
            new Message(2000, new Dictionary<string, byte[]>(), null, null)
        };
        var consumer = new TestConsumer(topics, messages: messages);
        consumer.GetTopics();

        var options = new FetchOptions(FetchPosition.Start, 10);

        // Act
        var result = await consumer.GetMessagesAsync("topic1", options);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetMessagesAsync_WithPartition_ReturnsMessages()
    {
        // Arrange
        var topics = new List<Topic> { new Topic("topic1", 2) };
        var messages = new List<Message>
        {
            new Message(1000, new Dictionary<string, byte[]>(), null, null)
        };
        var consumer = new TestConsumer(topics, messages: messages);
        consumer.GetTopics();

        var options = new FetchOptions(FetchPosition.Start, 10);

        // Act
        var result = await consumer.GetMessagesAsync("topic1", 0, options);

        // Assert
        Assert.Single(result);
    }

    #endregion ValidateTopic

    #region Dispose

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        // Arrange
        var consumer = new TestConsumer(new List<Topic>());

        // Act & Assert
        var exception = Record.Exception(() => consumer.Dispose());
        Assert.Null(exception);
    }

    #endregion Dispose

    private class ThrowingConsumer : ConsumerBase
    {
        public override bool ValidateConnection() => false;

        protected override List<Topic> FetchTopics()
        {
            throw new InvalidOperationException("Connection failed");
        }

        protected override Task GetMessagesAsync(string topicName, FetchOptions options, MessageStream messages, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        protected override Task GetMessagesAsync(string topicName, int partition, FetchOptions options, MessageStream messages, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private class ConcurrentLoadConsumer(List<Topic> topics) : ConsumerBase
    {
        private int fetchCount;
        public int FetchCount => fetchCount;

        public override bool ValidateConnection() => true;

        protected override List<Topic> FetchTopics()
        {
            Interlocked.Increment(ref fetchCount);
            Thread.Sleep(50);
            return topics;
        }

        protected override Task GetMessagesAsync(string topicName, FetchOptions options, MessageStream messages,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        protected override Task GetMessagesAsync(string topicName, int partition, FetchOptions options,
            MessageStream messages, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
