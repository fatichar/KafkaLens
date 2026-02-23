using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
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
        private static Func<IConsumer<byte[], byte[]>>? _consumerFactory;
        private static IAdminClient? _nextMockAdminClient;

        public Func<IEnumerable<TopicPartitionOffsetSpec>, Task<ListOffsetsResult>> ListOffsetsHandler { get; set; } =
            _ => Task.FromResult(new ListOffsetsResult { ResultInfos = new() });

        private TestConfluentConsumer(string url)
            : base(url, new KafkaConfig())
        {
        }

        public static TestConfluentConsumer Create(string url, Func<IConsumer<byte[], byte[]>>? consumerFactory, IAdminClient mockAdminClient)
        {
            _consumerFactory = consumerFactory;
            _nextMockAdminClient = mockAdminClient;
            return new TestConfluentConsumer(url);
        }

        protected override IConsumer<byte[], byte[]> CreateConsumer() => _consumerFactory?.Invoke() ?? Substitute.For<IConsumer<byte[], byte[]>>();
        protected override IAdminClient CreateAdminClient(string url) => _nextMockAdminClient ?? Substitute.For<IAdminClient>();

        protected override Task<ListOffsetsResult> ListOffsetsAsync(IEnumerable<TopicPartitionOffsetSpec> topicPartitionOffsets, ListOffsetsOptions options)
        {
            return ListOffsetsHandler.Invoke(topicPartitionOffsets);
        }

        public async Task PublicGetMessages(List<TopicPartition> tps, FetchOptions options, MessageStream messages, CancellationToken cancellationToken)
        {
            // We need to use reflection because the method is private
            var method = typeof(ConfluentConsumer).GetMethod("GetMessagesAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(List<TopicPartition>), typeof(FetchOptions), typeof(MessageStream), typeof(CancellationToken) },
                null);
            Assert.NotNull(method);
            var task = method.Invoke(this, new object[] { tps, options, messages, cancellationToken }) as Task;
            Assert.NotNull(task);
            await task;
        }

        public async Task<List<WatermarkOffsets>> PublicQueryWatermarkOffsetsAsync(List<TopicPartition> tps, CancellationToken cancellationToken)
        {
            var method = typeof(ConfluentConsumer).GetMethod("QueryWatermarkOffsetsAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(List<TopicPartition>), typeof(CancellationToken) },
                null);
            Assert.NotNull(method);
            var task = method.Invoke(this, new object[] { tps, cancellationToken }) as Task<List<WatermarkOffsets>>;
            Assert.NotNull(task);
            return await task;
        }
    }

    [Fact]
    public async Task FetchMessages_WhenFirstPollTimesOut_RetriesAndStillFetches()
    {
        // Arrange
        var mockAdminClient = Substitute.For<IAdminClient>();
        var pollCount = 0;

        Func<IConsumer<byte[], byte[]>> consumerFactory = () =>
        {
            var mockConsumer = Substitute.For<IConsumer<byte[], byte[]>>();
            mockConsumer.Consume(Arg.Any<TimeSpan>()).Returns(_ =>
            {
                var current = Interlocked.Increment(ref pollCount);
                if (current == 1)
                {
                    return null;
                }

                return new ConsumeResult<byte[], byte[]>
                {
                    Message = new Message<byte[], byte[]>
                    {
                        Key = Array.Empty<byte>(),
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
        consumer.ListOffsetsHandler = specs =>
        {
            var isEarliest = specs.First().OffsetSpec.GetType().Name.Contains("Earliest");
            long offset = isEarliest ? 0 : 100;
            return Task.FromResult(new ListOffsetsResult
            {
                ResultInfos = specs.Select(s => new ListOffsetsResultInfo
                {
                    TopicPartitionOffsetError = new TopicPartitionOffsetError(s.TopicPartition, new Offset(offset), new Error(ErrorCode.NoError), 0)
                }).ToList()
            });
        };

        var tps = new List<TopicPartition> { new("test-topic", 0) };
        var options = new FetchOptions(FetchPosition.Start, 1);
        var messages = new MessageStream();

        // Act
        await consumer.PublicGetMessages(tps, options, messages, CancellationToken.None);

        // Assert
        Assert.Single(messages.Messages);
        Assert.Equal(2, pollCount);
    }

    [Fact]
    public async Task FetchMessages_FromMultiplePartitions_IsNowParallel()
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
                        Key = Array.Empty<byte>(),
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
        consumer.ListOffsetsHandler = specs =>
        {
            var isEarliest = specs.First().OffsetSpec.GetType().Name.Contains("Earliest");
            long offset = isEarliest ? 0 : 100;
            return Task.FromResult(new ListOffsetsResult
            {
                ResultInfos = specs.Select(s => new ListOffsetsResultInfo
                {
                    TopicPartitionOffsetError = new TopicPartitionOffsetError(s.TopicPartition, new Offset(offset), new Error(ErrorCode.NoError), 0)
                }).ToList()
            });
        };

        var topic = "test-topic";
        var tps = new List<TopicPartition>
        {
            new TopicPartition(topic, 0),
            new TopicPartition(topic, 1)
        };

        var options = new FetchOptions(FetchPosition.Start, 2); // 1 message per partition (2 partitions)
        var messages = new MessageStream();

        // Act
        var sw = Stopwatch.StartNew();
        await consumer.PublicGetMessages(tps, options, messages, CancellationToken.None);
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

    [Fact]
    public async Task QueryWatermarkOffsets_Batch_IsParallel()
    {
        // Arrange
        var mockAdminClient = Substitute.For<IAdminClient>();
        int queryDelayMs = 200;
        int callCount = 0;

        var consumer = TestConfluentConsumer.Create("localhost:9092", null, mockAdminClient);
        consumer.ListOffsetsHandler = async specs =>
        {
            Interlocked.Increment(ref callCount);
            await Task.Delay(queryDelayMs);
            var result = new ListOffsetsResult
            {
                ResultInfos = specs.Select(s => new ListOffsetsResultInfo
                {
                    TopicPartitionOffsetError = new TopicPartitionOffsetError(s.TopicPartition, new Offset(100), new Error(ErrorCode.NoError), 0)
                }).ToList()
            };
            return result;
        };

        var topic = "test-topic";
        int partitionCount = 5;
        var tps = new List<TopicPartition>();
        for (int i = 0; i < partitionCount; i++)
        {
            tps.Add(new TopicPartition(topic, i));
        }

        // Act
        var sw = Stopwatch.StartNew();
        var results = await consumer.PublicQueryWatermarkOffsetsAsync(tps, CancellationToken.None);
        sw.Stop();

        // Assert
        Assert.Equal(partitionCount, results.Count);
        // We expect exactly 2 calls to ListOffsetsAsync (one for Earliest, one for Latest)
        Assert.Equal(2, callCount);

        // Since we make TWO calls in parallel, total time should be ~queryDelayMs.

        Console.WriteLine($"Batch query took: {sw.ElapsedMilliseconds}ms (Expected ~{queryDelayMs}ms)");

        // Parallel batching should be much faster than sequential individual queries (which took ~1000ms)
        Assert.True(sw.ElapsedMilliseconds < partitionCount * queryDelayMs / 2, $"Expected parallel execution taking ~{queryDelayMs}ms, but took {sw.ElapsedMilliseconds}ms");
    }
}
