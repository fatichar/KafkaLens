using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Confluent.Kafka;
using KafkaLens.Core.Services;
using KafkaLens.Shared.Models;
using NSubstitute;
using Xunit;
using TopicPartition = Confluent.Kafka.TopicPartition;

namespace KafkaLens.Core.Tests.Services;

public class ConfluentConsumerTests
{
    private class TestConfluentConsumer : ConfluentConsumer
    {
        private static Func<IConsumer<byte[], byte[]>> _consumerFactory;
        private static IAdminClient _nextMockAdminClient;

        private TestConfluentConsumer(string url)
            : base(url)
        {
        }

        public static TestConfluentConsumer Create(string url, Func<IConsumer<byte[], byte[]>> consumerFactory, IAdminClient mockAdminClient)
        {
            _consumerFactory = consumerFactory;
            _nextMockAdminClient = mockAdminClient;
            return new TestConfluentConsumer(url);
        }

        protected override IConsumer<byte[], byte[]> CreateConsumer() => _consumerFactory?.Invoke() ?? Substitute.For<IConsumer<byte[], byte[]>>();
        protected override IAdminClient CreateAdminClient(string url) => _nextMockAdminClient ?? Substitute.For<IAdminClient>();

        public void PublicGetMessages(List<TopicPartition> tps, FetchOptions options, MessageStream messages, CancellationToken cancellationToken)
        {
            // We need to use reflection because the method is private
            var method = typeof(ConfluentConsumer).GetMethod("GetMessages",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(List<TopicPartition>), typeof(FetchOptions), typeof(MessageStream), typeof(CancellationToken) },
                null);
            method.Invoke(this, new object[] { tps, options, messages, cancellationToken });
        }
    }

    [Fact]
    public void FetchMessages_FromMultiplePartitions_IsNowParallel()
    {
        // Arrange
        var mockAdminClient = Substitute.For<IAdminClient>();
        int consumeDelayMs = 500;
        int consumeCount = 0;

        Func<IConsumer<byte[], byte[]>> consumerFactory = () =>
        {
            var mockConsumer = Substitute.For<IConsumer<byte[], byte[]>>();
            mockConsumer.QueryWatermarkOffsets(Arg.Any<TopicPartition>(), Arg.Any<TimeSpan>())
                .Returns(new WatermarkOffsets(0, 100));
            mockConsumer.Consume(Arg.Any<TimeSpan>()).Returns(x =>
            {
                Interlocked.Increment(ref consumeCount);
                Thread.Sleep(consumeDelayMs);
                return new ConsumeResult<byte[], byte[]>
                {
                    Message = new Message<byte[], byte[]>
                    {
                        Key = null,
                        Value = Array.Empty<byte>(),
                        Timestamp = new Timestamp(DateTime.Now),
                        Headers = new Headers()
                    },
                    Partition = 0,
                    Offset = 0
                };
            });
            return mockConsumer;
        };

        var consumer = TestConfluentConsumer.Create("localhost:9092", consumerFactory, mockAdminClient);

        var topic = "test-topic";
        var tps = new List<TopicPartition>
        {
            new TopicPartition(topic, 0),
            new TopicPartition(topic, 1)
        };

        var options = new FetchOptions(FetchPosition.START, 2); // 1 message per partition (2 partitions)
        var messages = new MessageStream();

        // Act
        var sw = Stopwatch.StartNew();
        consumer.PublicGetMessages(tps, options, messages, CancellationToken.None);
        sw.Stop();

        // Assert
        Console.WriteLine($"Messages count: {messages.Messages.Count}");
        Console.WriteLine($"Consume count: {consumeCount}");
        Assert.Equal(2, messages.Messages.Count);
        Assert.Equal(2, consumeCount);
        // With 2 partitions and 500ms delay each, it should take ~1000ms if serial, ~500ms if parallel.
        // After optimization, it should take ~500ms.
        // We want to verify it takes < 800ms (to give some margin for thread overhead).
        Assert.True(sw.ElapsedMilliseconds < 2 * consumeDelayMs - 200, $"Expected parallel execution taking ~500ms, but took {sw.ElapsedMilliseconds}ms");
    }
}
