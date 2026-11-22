using System;
using System.Collections.Concurrent;
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
        public Func<IEnumerable<TopicPartitionOffsetSpec>, Task<ListOffsetsResult>> ListOffsetsHandler { get; set; } =
            _ => Task.FromResult(new ListOffsetsResult { ResultInfos = new() });

        public ConcurrentBag<TimeSpan?> OffsetRequestTimeouts { get; } = new();

        private TestConfluentConsumer(string url, Func<IConsumer<byte[], byte[]>>? consumerFactory, IAdminClient mockAdminClient, KafkaConfig? config)
            : base(url, config ?? new KafkaConfig(), mockAdminClient, consumerFactory ?? (() => Substitute.For<IConsumer<byte[], byte[]>>()))
        {
        }

        public static TestConfluentConsumer Create(string url, Func<IConsumer<byte[], byte[]>>? consumerFactory, IAdminClient mockAdminClient, KafkaConfig? config = null)
        {
            return new TestConfluentConsumer(url, consumerFactory, mockAdminClient, config);
        }

        protected override Task<ListOffsetsResult> ListOffsetsAsync(IEnumerable<TopicPartitionOffsetSpec> topicPartitionOffsets, ListOffsetsOptions options)
        {
            OffsetRequestTimeouts.Add(options.RequestTimeout);
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

    private static IAdminClient ValidationAdmin(bool hasTopic = true)
    {
        var admin = Substitute.For<IAdminClient>();
        var topics = hasTopic
            ? new List<TopicMetadata> { new("probe", new List<PartitionMetadata>
                { new(0, 1, new[] { 1 }, new[] { 1 }, new Error(ErrorCode.NoError)) }, new Error(ErrorCode.NoError)) }
            : new List<TopicMetadata>();
        admin.GetMetadata(Arg.Any<TimeSpan>()).Returns(new Metadata(
            new List<BrokerMetadata> { new(1, "broker", 9092) }, topics, 1, "broker"));
        return admin;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OffsetRequest_TimeoutOrCancellationKeepsAdminAliveUntilLateFaults(bool cancel)
    {
        var admin = Substitute.For<IAdminClient>();
        var disposedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        admin.When(x => x.Dispose()).Do(_ => disposedSignal.TrySetResult());
        var earliest = new TaskCompletionSource<ListOffsetsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var latest = new TaskCompletionSource<ListOffsetsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var config = new KafkaConfig { QueryWatermarkTimeoutMs = cancel ? 10000 : 20 };
        using var consumer = TestConfluentConsumer.Create("broker:9092", null, admin, config);
        consumer.ListOffsetsHandler = specs => specs.First().OffsetSpec.GetType().Name.Contains("Earliest")
            ? earliest.Task : latest.Task;
        using var cancellation = new CancellationTokenSource();
        var query = consumer.PublicQueryWatermarkOffsetsAsync(new List<TopicPartition> { new("probe", 0) }, cancellation.Token);
        if (cancel)
            cancellation.Cancel();

        try
        {
            if (cancel)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.WaitAsync(TimeSpan.FromSeconds(5)));
            else
                await Assert.ThrowsAsync<TimeoutException>(() => query.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(query.IsCompleted);
            Assert.Equal(2, consumer.OffsetRequestTimeouts.Count);
            Assert.All(consumer.OffsetRequestTimeouts, timeout => Assert.Equal(TimeSpan.FromMilliseconds(config.QueryWatermarkTimeoutMs), timeout));
            consumer.Dispose();
            admin.DidNotReceive().Dispose();
            earliest.TrySetException(new KafkaException(new Error(ErrorCode.Local_TimedOut, "Late earliest failure")));
            admin.DidNotReceive().Dispose();
        }
        finally
        {
            earliest.TrySetException(new KafkaException(new Error(ErrorCode.Local_TimedOut)));
            latest.TrySetException(new KafkaException(new Error(ErrorCode.Local_TimedOut, "Late latest failure")));
            consumer.Dispose();
        }
        await disposedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        admin.Received(1).Dispose();
    }

    [Fact]
    public async Task Dispose_DoesNotDisposeAdminDuringNativeMetadataCall()
    {
        var metadata = ValidationAdmin(false).GetMetadata(TimeSpan.Zero);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new BlockingCollection<bool>();
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var admin = Substitute.For<IAdminClient>();
        admin.GetMetadata(Arg.Any<TimeSpan>()).Returns(_ =>
        {
            entered.TrySetResult();
            release.Take(guard.Token);
            return metadata;
        });
        admin.When(x => x.Dispose()).Do(_ => disposedSignal.TrySetResult());
        using var consumer = new ConfluentConsumer("broker:9092", new KafkaConfig(), admin,
            () => throw new InvalidOperationException("No consumer needed"));
        var validation = consumer.ValidateConnectionWithDetailsAsync();
        await entered.Task.WaitAsync(guard.Token);
        try
        {
            consumer.Dispose();
            admin.DidNotReceive().Dispose();
        }
        finally
        {
            release.Add(true);
        }
        await validation.WaitAsync(guard.Token);
        await disposedSignal.Task.WaitAsync(guard.Token);
        admin.Received(1).Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Validation_UsesFreshMetadataInsteadOfCachedTopics(bool synchronous)
    {
        var admin = ValidationAdmin();
        using var consumer = new ConfluentConsumer("broker:9092", new KafkaConfig(), admin,
            () => throw new InvalidOperationException("Cached topic must not be probed"));
        Assert.Single(consumer.GetTopics());
        var emptyMetadata = ValidationAdmin(false).GetMetadata(TimeSpan.Zero);
        admin.GetMetadata(Arg.Any<TimeSpan>()).Returns(emptyMetadata);

        var validation = synchronous
            ? consumer.ValidateConnectionWithDetails()
            : await consumer.ValidateConnectionWithDetailsAsync();

        Assert.True(validation.Succeeded);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Validation_RejectsUnavailablePartitionsAndTopicErrors(bool synchronous, bool topicError)
    {
        var admin = Substitute.For<IAdminClient>();
        var metadata = new Metadata(new List<BrokerMetadata> { new(1, "broker", 9092) },
            new List<TopicMetadata> { new("probe", new List<PartitionMetadata>(),
                new Error(topicError ? ErrorCode.TopicAuthorizationFailed : ErrorCode.NoError)) }, 1, "broker");
        admin.GetMetadata(Arg.Any<TimeSpan>()).Returns(metadata);
        using var consumer = new ConfluentConsumer("broker:9092", new KafkaConfig(), admin,
            () => throw new InvalidOperationException("No available partition"));

        var validation = synchronous
            ? consumer.ValidateConnectionWithDetails()
            : await consumer.ValidateConnectionWithDetailsAsync();

        Assert.False(validation.Succeeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Validation_RequiresSuccessfulConsumerProbe(bool probeFails)
    {
        var native = Substitute.For<IConsumer<byte[], byte[]>>();
        native.QueryWatermarkOffsets(Arg.Any<TopicPartition>(), Arg.Any<TimeSpan>()).Returns(_ => probeFails
            ? throw new KafkaException(new Error(ErrorCode.Local_Transport, "Probe disconnected"))
            : new WatermarkOffsets(0, 100));
        using var consumer = new ConfluentConsumer("broker:9092", new KafkaConfig(), ValidationAdmin(), () => native);

        var result = await consumer.ValidateConnectionWithDetailsAsync();

        Assert.Equal(!probeFails, result.Succeeded);
        native.Received(1).QueryWatermarkOffsets(new TopicPartition("probe", 0), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Validation_SynchronousCompatibilityUsesOwnedConsumerOnCallingThread()
    {
        var thread = Environment.CurrentManagedThreadId;
        var native = Substitute.For<IConsumer<byte[], byte[]>>();
        native.QueryWatermarkOffsets(Arg.Any<TopicPartition>(), Arg.Any<TimeSpan>()).Returns(_ =>
        {
            Assert.Equal(thread, Environment.CurrentManagedThreadId);
            return new WatermarkOffsets(0, 100);
        });
        using var consumer = new ConfluentConsumer("broker:9092", new KafkaConfig(), ValidationAdmin(), () => native);

        Assert.True(consumer.ValidateConnectionWithDetails().Succeeded);
        native.Received(1).Dispose();
    }

    [Fact]
    public async Task Validation_PreCanceled_DoesNotCallNativeMetadata()
    {
        var admin = ValidationAdmin();
        using var consumer = new ConfluentConsumer("broker:9092", new KafkaConfig(), admin,
            () => throw new InvalidOperationException("Must not create consumer"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => consumer.ValidateConnectionWithDetailsAsync(cancellation.Token));

        admin.DidNotReceive().GetMetadata(Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task Validation_NoTopics_UsesMetadataWithoutCreatingConsumer()
    {
        using var consumer = new ConfluentConsumer("broker:9092", new KafkaConfig(), ValidationAdmin(false),
            () => throw new InvalidOperationException("Must not lease"));

        Assert.True((await consumer.ValidateConnectionWithDetailsAsync()).Succeeded);
    }

    [Fact]
    public async Task Validation_CancellationDuringProbe_DoesNotReleaseLeaseUntilNativeCallFinishes()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new BlockingCollection<bool>();
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var native = Substitute.For<IConsumer<byte[], byte[]>>();
        native.QueryWatermarkOffsets(Arg.Any<TopicPartition>(), Arg.Any<TimeSpan>()).Returns(_ =>
        {
            entered.TrySetResult();
            Assert.True(release.Take(guard.Token));
            return new WatermarkOffsets(0, 100);
        });
        using var consumer = new ConfluentConsumer("broker:9092", new KafkaConfig(), ValidationAdmin(), () => native);
        using var cancellation = new CancellationTokenSource();
        var validation = consumer.ValidateConnectionWithDetailsAsync(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        try
        {
            Assert.False(validation.IsCompleted);
            native.DidNotReceive().Dispose();
        }
        finally
        {
            release.Add(true);
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => validation);
    }

    [Fact]
    public async Task Validation_PoolTimeoutReturnsFailure_AndDoesNotOverReleaseLeases()
    {
        using var consumer = new ConfluentConsumer("broker:9092", new KafkaConfig { QueryWatermarkTimeoutMs = 20 },
            ValidationAdmin(), () => Substitute.For<IConsumer<byte[], byte[]>>());
        var field = typeof(ConfluentConsumer).GetField("consumerPool",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var pool = (ConfluentConsumer.ConsumerPool)field.GetValue(consumer)!;
        var leases = new List<ConfluentConsumer.ConsumerLease>();
        try
        {
            for (var i = 0; i < 10; i++)
                leases.Add(await pool.LeaseAsync(CancellationToken.None));

            var result = await consumer.ValidateConnectionWithDetailsAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(result.Succeeded);
            Assert.Contains("Timed out", result.ErrorMessage);
            using var cancellation = new CancellationTokenSource();
            var waiting = pool.LeaseAsync(cancellation.Token);
            Assert.False(waiting.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }
        finally
        {
            foreach (var lease in leases)
                lease.Dispose();
        }
    }

    [Fact]
    public async Task Validation_NativeTimeoutReturnsDiagnosticFailure()
    {
        var native = Substitute.For<IConsumer<byte[], byte[]>>();
        native.QueryWatermarkOffsets(Arg.Any<TopicPartition>(), Arg.Any<TimeSpan>())
            .Returns(_ => throw new KafkaException(new Error(ErrorCode.Local_TimedOut, "Watermark timeout")));
        using var consumer = new ConfluentConsumer("broker:9092", new KafkaConfig(), ValidationAdmin(), () => native);

        var result = await consumer.ValidateConnectionWithDetailsAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("Watermark timeout", result.ErrorMessage);
    }

    [Fact]
    public async Task Pool_FactoryFailureRestoresPermit_AndLeaseReturnsExactlyOnce()
    {
        var calls = 0;
        var native = Substitute.For<IConsumer<byte[], byte[]>>();
        using var pool = new ConfluentConsumer.ConsumerPool(1, () => Interlocked.Increment(ref calls) == 1
            ? throw new InvalidOperationException("Creation failed") : native, false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pool.LeaseAsync(CancellationToken.None));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var lease = await pool.LeaseAsync(timeout.Token);
        lease.Dispose();
        await lease.DisposeAsync();
        using var next = await pool.LeaseAsync(timeout.Token);
        Assert.Same(native, next.Consumer);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Pool_WaitingLeaseCanBeCanceled_AndDisposeDefersLeasedConsumerDisposal()
    {
        var native = Substitute.For<IConsumer<byte[], byte[]>>();
        var pool = new ConfluentConsumer.ConsumerPool(1, () => native, false);
        var lease = await pool.LeaseAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var waiting = pool.LeaseAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        pool.Dispose();
        native.DidNotReceive().Dispose();
        lease.Dispose();
        lease.Dispose();
        native.Received(1).Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FetchMessages_FailureIsNotSuccessfulEmptyResult(bool timeout)
    {
        var native = Substitute.For<IConsumer<byte[], byte[]>>();
        native.Consume(Arg.Any<TimeSpan>()).Returns(_ => timeout
            ? null!
            : throw new KafkaException(new Error(ErrorCode.Local_Transport, "Disconnected")));
        using var consumer = TestConfluentConsumer.Create("localhost:9092", () => native, Substitute.For<IAdminClient>());
        consumer.ListOffsetsHandler = specs => Task.FromResult(new ListOffsetsResult
        {
            ResultInfos = specs.Select(s => new ListOffsetsResultInfo
            {
                TopicPartitionOffsetError = new TopicPartitionOffsetError(s.TopicPartition,
                    s.OffsetSpec.GetType().Name.Contains("Earliest") ? 0 : 100, new Error(ErrorCode.NoError), 0)
            }).ToList()
        });

        var failure = await Record.ExceptionAsync(() => consumer.PublicGetMessages(
            new List<TopicPartition> { new("test-topic", 0) }, new FetchOptions(FetchPosition.Start, 1),
            new MessageStream(), CancellationToken.None));

        Assert.NotNull(failure);
        if (timeout)
            Assert.IsType<TimeoutException>(failure);
        else
            Assert.IsType<KafkaException>(failure);
        native.Received(1).Unassign();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task MessageStream_ReportsFailureOrCancellation_AndPreservesEofSuccess(int outcome)
    {
        using var cancellation = new CancellationTokenSource();
        var native = Substitute.For<IConsumer<byte[], byte[]>>();
        native.Consume(Arg.Any<TimeSpan>()).Returns(_ =>
        {
            if (outcome == 1)
                return null!;
            if (outcome == 2)
                throw new ConsumeException(new ConsumeResult<byte[], byte[]>(), new Error(ErrorCode.Local_Transport, "Disconnected"));
            if (outcome == 3)
                cancellation.Cancel();
            return new ConsumeResult<byte[], byte[]> { IsPartitionEOF = true };
        });
        using var consumer = TestConfluentConsumer.Create("broker:9092", () => native, ValidationAdmin());
        SetSuccessfulOffsets(consumer);
        var messages = consumer.GetMessageStream("probe", new FetchOptions(FetchPosition.Start, 1), cancellation.Token);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        messages.Finished += () => finished.TrySetResult();
        if (!messages.HasMore)
            finished.TrySetResult();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(messages.Messages);
        Assert.Equal(outcome == 3, messages.WasCanceled);
        if (outcome == 1)
            Assert.IsType<TimeoutException>(messages.Error);
        else if (outcome == 2)
            Assert.IsType<ConsumeException>(messages.Error);
        else
            Assert.Null(messages.Error);
        native.Received(1).Unassign();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ChannelStream_ReportsFailures_AndPreservesEofSuccess(int outcome)
    {
        var native = Substitute.For<IConsumer<byte[], byte[]>>();
        native.Consume(Arg.Any<TimeSpan>()).Returns(_ => outcome switch
        {
            1 => null!,
            2 => throw new ConsumeException(new ConsumeResult<byte[], byte[]>(), new Error(ErrorCode.Local_Transport, "Disconnected")),
            _ => new ConsumeResult<byte[], byte[]> { IsPartitionEOF = true }
        });
        using var consumer = TestConfluentConsumer.Create("broker:9092", () => native, ValidationAdmin());
        SetSuccessfulOffsets(consumer);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var failure = await Record.ExceptionAsync(async () =>
        {
            await foreach (var message in consumer.StreamMessagesAsync("probe", new FetchOptions(FetchPosition.Start, 1), cancellation.Token))
                Assert.Fail("Expected no messages");
        });

        if (outcome == 1)
            Assert.IsType<TimeoutException>(failure);
        else if (outcome == 2)
            Assert.IsType<ConsumeException>(failure);
        else
            Assert.Null(failure);
        native.Received(1).Unassign();
    }

    private static void SetSuccessfulOffsets(TestConfluentConsumer consumer)
    {
        consumer.ListOffsetsHandler = specs => Task.FromResult(new ListOffsetsResult
        {
            ResultInfos = specs.Select(s => new ListOffsetsResultInfo
            {
                TopicPartitionOffsetError = new TopicPartitionOffsetError(s.TopicPartition,
                    s.OffsetSpec.GetType().Name.Contains("Earliest") ? 0 : 100, new Error(ErrorCode.NoError), 0)
            }).ToList()
        });
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

    [Fact]
    public async Task GetMessagesAsync_BackwardFetch_ByOffset_StartsFromCorrectOffset()
    {
        // Arrange
        var mockAdminClient = Substitute.For<IAdminClient>();
        var assignedOffset = -1L;
        var limit = 5;
        
        Func<IConsumer<byte[], byte[]>> consumerFactory = () =>
        {
            var mockConsumer = Substitute.For<IConsumer<byte[], byte[]>>();
            mockConsumer.QueryWatermarkOffsets(Arg.Any<TopicPartition>(), Arg.Any<TimeSpan>())
                .Returns(new WatermarkOffsets(0, 100)); // Log size is 100
                
            mockConsumer.When(x => x.Assign(Arg.Any<TopicPartitionOffset>()))
                .Do(x => assignedOffset = x.Arg<TopicPartitionOffset>().Offset.Value);

            mockConsumer.Consume(Arg.Any<TimeSpan>()).Returns((ConsumeResult<byte[], byte[]>?)null); // Just timeout immediately
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

        var tps = new List<TopicPartition> { new TopicPartition("test-topic", 0) };
        var options = new FetchOptions(new FetchPosition(PositionType.Offset, 50), limit)
        {
            Direction = FetchDirection.Backward
        };
        var messages = new MessageStream();

        // Act
        await Assert.ThrowsAsync<TimeoutException>(() => consumer.PublicGetMessages(tps, options, messages, CancellationToken.None));

        // Assert
        // Starting at 50, backwards for 5 items, means we want offsets 46, 47, 48, 49, 50.
        // So the assigned offset should be 46.
        Assert.Equal(46, assignedOffset);
    }

    [Fact]
    public async Task GetMessagesAsync_BackwardFetch_ByTimestamp_UsesOffsetPlusOneForTimesAndCalculatesCorrectly()
    {
        // Arrange
        var mockAdminClient = Substitute.For<IAdminClient>();
        var assignedOffset = -1L;
        var limit = 5;
        var queriedTimestamp = -1L;
        
        Func<IConsumer<byte[], byte[]>> consumerFactory = () =>
        {
            var mockConsumer = Substitute.For<IConsumer<byte[], byte[]>>();
            mockConsumer.QueryWatermarkOffsets(Arg.Any<TopicPartition>(), Arg.Any<TimeSpan>())
                .Returns(new WatermarkOffsets(0, 100)); // Log size is 100
                
            mockConsumer.OffsetsForTimes(Arg.Any<IEnumerable<TopicPartitionTimestamp>>(), Arg.Any<TimeSpan>())
                .Returns(args =>
                {
                    var query = args.Arg<IEnumerable<TopicPartitionTimestamp>>().First();
                    queriedTimestamp = query.Timestamp.UnixTimestampMs;
                    // Simulate that timestamp (query.Timestamp) corresponds to offset 50
                    return new List<TopicPartitionOffset> { new TopicPartitionOffset(query.TopicPartition, new Offset(50)) };
                });

            mockConsumer.When(x => x.Assign(Arg.Any<TopicPartitionOffset>()))
                .Do(x => assignedOffset = x.Arg<TopicPartitionOffset>().Offset.Value);

            mockConsumer.Consume(Arg.Any<TimeSpan>()).Returns((ConsumeResult<byte[], byte[]>?)null); // Return nothing
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

        var tps = new List<TopicPartition> { new TopicPartition("test-topic", 0) };
        var targetTimestamp = 1600000000L;
        var options = new FetchOptions(new FetchPosition(PositionType.Timestamp, targetTimestamp), limit)
        {
            Direction = FetchDirection.Backward
        };
        var messages = new MessageStream();

        // Act
        await Assert.ThrowsAsync<TimeoutException>(() => consumer.PublicGetMessages(tps, options, messages, CancellationToken.None));

        // Assert
        // We requested T, backwards. Code should query OffsetsForTimes for T+1.
        Assert.Equal(targetTimestamp + 1, queriedTimestamp);
        // OffsetsForTimes returned offset 50. Limit is 5. Backward fetch should start at 50 - 5 = 45.
        Assert.Equal(45, assignedOffset);
    }
}