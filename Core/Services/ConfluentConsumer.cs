using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using KafkaLens.Core.Utils;
using KafkaLens.Shared.Models;
using Serilog;
using TopicPartition = Confluent.Kafka.TopicPartition;

namespace KafkaLens.Core.Services;

internal class ConfluentConsumer : ConsumerBase, IStreamingKafkaConsumer, IDisposable
{
    private const int MAX_PARTITION_COUNT = 10000;
    private const int STREAM_CHANNEL_CAPACITY = 512;
    private readonly TimeSpan queryWatermarkTimeout;
    private readonly TimeSpan queryTopicsTimeout;
    private readonly TimeSpan consumeTimeout;
    private readonly KafkaConfig kafkaConfig;

    private readonly ConsumerPool consumerPool;
    private readonly Func<IConsumer<byte[], byte[]>> consumerFactory;
    private readonly object adminLifetimeGate = new();
    private int activeAdminOperations;
    private int disposed;
    private ConsumerConfig Config { get; set; }

    private IAdminClient AdminClient { get; }

    internal sealed class ConsumerPool : IDisposable
    {
        private readonly Func<IConsumer<byte[], byte[]>> factory;
        private readonly ConcurrentBag<IConsumer<byte[], byte[]>> pool = new();
        private readonly SemaphoreSlim semaphore;
        private readonly object gate = new();
        private bool disposed;

        public ConsumerPool(int maxLimit, Func<IConsumer<byte[], byte[]>> factory, bool warmUp = true)
        {
            this.factory = factory;
            semaphore = new SemaphoreSlim(maxLimit, maxLimit);

            // Warm-up: Pre-create one consumer so the very first fetch doesn't pay the initialization penalty.
            // This happens in the background.
            if (warmUp)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var lease = await LeaseAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Log.Error(e, "Failed to warm up consumer pool");
                    }
                });
        }

        public async Task<ConsumerLease> LeaseAsync(CancellationToken ct)
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            IConsumer<byte[], byte[]>? consumer = null;
            try
            {
                lock (gate)
                {
                    ObjectDisposedException.ThrowIf(disposed, this);
                    pool.TryTake(out consumer);
                }
                consumer ??= await Task.Run(factory, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                lock (gate)
                {
                    ObjectDisposedException.ThrowIf(disposed, this);
                    return new ConsumerLease(consumer, this);
                }
            }
            catch
            {
                if (consumer != null)
                    Return(consumer);
                else
                    semaphore.Release();
                throw;
            }
        }

        public void Return(IConsumer<byte[], byte[]> consumer)
        {
            try
            {
                lock (gate)
                {
                    if (!disposed)
                    {
                        pool.Add(consumer);
                        return;
                    }
                }
                consumer.Dispose();
            }
            finally
            {
                semaphore.Release();
            }
        }

        public void Dispose()
        {
            List<IConsumer<byte[], byte[]>> idle = new();
            lock (gate)
            {
                if (disposed)
                    return;
                disposed = true;
                while (pool.TryTake(out var consumer))
                    idle.Add(consumer);
            }
            foreach (var consumer in idle)
                consumer.Dispose();
        }
    }

    internal sealed class ConsumerLease : IAsyncDisposable, IDisposable
    {
        public IConsumer<byte[], byte[]> Consumer { get; }
        private ConsumerPool? pool;

        public ConsumerLease(IConsumer<byte[], byte[]> consumer, ConsumerPool pool)
        {
            Consumer = consumer;
            this.pool = pool;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref pool, null)?.Return(Consumer);
        }
    }

    #region Create

    internal ConfluentConsumer(string url, KafkaConfig kafkaConfig,
        IAdminClient? adminClient = null, Func<IConsumer<byte[], byte[]>>? consumerFactory = null)
    {
        this.kafkaConfig = kafkaConfig;
        queryWatermarkTimeout = TimeSpan.FromMilliseconds(kafkaConfig.QueryWatermarkTimeoutMs);
        queryTopicsTimeout = TimeSpan.FromMilliseconds(kafkaConfig.QueryTopicsTimeoutMs);
        consumeTimeout = TimeSpan.FromMilliseconds(kafkaConfig.ConsumeTimeoutMs);

        Config = CreateConsumerConfig(url);
        Config.Set("log_level", "0");
        AdminClient = adminClient ?? CreateAdminClient(Config.BootstrapServers);
        this.consumerFactory = consumerFactory ?? CreateConsumer;
        consumerPool = new ConsumerPool(10, this.consumerFactory, consumerFactory == null); // Max limit of 10 concurrent consumers
    }

    protected virtual IConsumer<byte[], byte[]> CreateConsumer()
    {
        return new ConsumerBuilder<byte[], byte[]>(Config)
            .SetLogHandler((c, m) => { })
            .SetErrorHandler((c, e) => { })
            .Build();
    }

    private ConsumerConfig CreateConsumerConfig(String url)
    {
        return new ConsumerConfig
        {
            GroupId = kafkaConfig.GroupId,
            ClientId = "KafkaLens.Server",
            BootstrapServers = url,
            EnableAutoOffsetStore = kafkaConfig.EnableAutoOffsetStore,
            EnableAutoCommit = kafkaConfig.EnableAutoCommit,
            FetchMaxBytes = kafkaConfig.FetchMaxBytes,
            StatisticsIntervalMs = kafkaConfig.StatisticsIntervalMs,
            LogQueue = true,
            EnablePartitionEof = true
        };
    }

    protected virtual IAdminClient CreateAdminClient(string url)
    {
        var config = new AdminClientConfig
        {
            BootstrapServers = url
        };
        config.Set("log_level", "0");
        return new AdminClientBuilder(config)
            .SetLogHandler((c, m) => { })
            .SetErrorHandler((c, e) => { })
            .Build();
    }

    protected virtual Task<ListOffsetsResult> ListOffsetsAsync(
        IEnumerable<TopicPartitionOffsetSpec> topicPartitionOffsets, ListOffsetsOptions options)
    {
        return AdminClient.ListOffsetsAsync(topicPartitionOffsets, options);
    }

    #endregion Create

    #region Read

    public override bool ValidateConnection()
    {
        return ValidateConnectionWithDetails().Succeeded;
    }

    public override ConnectionValidationResult ValidateConnectionWithDetails()
    {
        Metadata metadata;
        try
        {
            metadata = GetMetadata(TimeSpan.FromMilliseconds(kafkaConfig.AdminMetadataTimeoutMs));
        }
        catch (Exception e)
        {
            Log.Error(e, "Connection validation failed");
            return ConnectionValidationResult.Failed(e.Message, e.ToString());
        }

        // AdminClient metadata can report success from a stale/cached broker connection even
        // after the underlying network path has silently dropped (e.g. a VPN disconnect that
        // doesn't immediately close the TCP socket). Confirm with a real consumer-side
        // watermark query, which uses a separate connection and forces a live round trip.
        return ProbeWithConsumer(metadata);
    }

    private ConnectionValidationResult ProbeWithConsumer(Metadata metadata)
    {
        TopicPartition? topic;
        try
        {
            topic = SelectValidationPartition(metadata);
        }
        catch (Exception e)
        {
            Log.Error(e, "Connection validation failed while listing topics");
            return ConnectionValidationResult.Failed(e.Message, e.ToString());
        }

        if (topic == null)
        {
            // No topic to probe against; the broker metadata check above is the best signal we have.
            return ConnectionValidationResult.Success();
        }

        try
        {
            using var consumer = consumerFactory();
            ProbeWithConsumer(consumer, topic, CancellationToken.None);
            return ConnectionValidationResult.Success();
        }
        catch (Exception e)
        {
            Log.Error(e, "Connection validation failed while probing topic {Topic}", topic.Topic);
            return ConnectionValidationResult.Failed(e.Message, e.ToString());
        }
    }

    public override Task<ConnectionValidationResult> ValidateConnectionWithDetailsAsync(CancellationToken cancellationToken = default)
        => Task.Run(async () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var metadata = GetMetadata(TimeSpan.FromMilliseconds(kafkaConfig.AdminMetadataTimeoutMs));
                cancellationToken.ThrowIfCancellationRequested();
                var topic = SelectValidationPartition(metadata);
                if (topic == null)
                    return ConnectionValidationResult.Success();

                using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                leaseCts.CancelAfter(queryWatermarkTimeout);
                ConsumerLease lease;
                try
                {
                    lease = await consumerPool.LeaseAsync(leaseCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return ConnectionValidationResult.Failed("Timed out waiting for an available consumer to validate connectivity.");
                }
                using (lease)
                {
                    ProbeWithConsumer(lease.Consumer, topic, cancellationToken);
                    return ConnectionValidationResult.Success();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Log.Error(e, "Connection validation failed");
                return ConnectionValidationResult.Failed(e.Message, e.ToString());
            }
        }, cancellationToken);

    private static TopicPartition? SelectValidationPartition(Metadata metadata)
    {
        if (metadata.OriginatingBrokerId == -1)
            throw new InvalidOperationException("No originating broker available to validate connectivity.");
        var topicError = metadata.Topics.FirstOrDefault(t => t.Error.IsError);
        if (topicError != null)
            throw new KafkaException(topicError.Error);
        var topic = metadata.Topics.FirstOrDefault(t => t.Partitions.Any(p => !p.Error.IsError));
        if (topic != null)
            return new TopicPartition(topic.Topic, topic.Partitions.First(p => !p.Error.IsError).PartitionId);
        if (metadata.Topics.Count == 0)
            return null;
        throw new InvalidOperationException("No available partition to validate connectivity.");
    }

    private void ProbeWithConsumer(IConsumer<byte[], byte[]> consumer, TopicPartition topic, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        consumer.QueryWatermarkOffsets(topic, queryWatermarkTimeout);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private Metadata GetMetadata(TimeSpan timeout)
    {
        BeginAdminOperation();
        try
        {
            return AdminClient.GetMetadata(timeout);
        }
        finally
        {
            EndAdminOperation();
        }
    }

    private void BeginAdminOperation()
    {
        lock (adminLifetimeGate)
        {
            ObjectDisposedException.ThrowIf(disposed != 0, this);
            activeAdminOperations++;
        }
    }

    private void EndAdminOperation()
    {
        bool disposeAdmin;
        lock (adminLifetimeGate)
        {
            activeAdminOperations--;
            disposeAdmin = disposed != 0 && activeAdminOperations == 0;
        }
        if (disposeAdmin)
            DisposeAdminClient();
    }

    private async Task<ListOffsetsResult> ListOffsetsWithLifetimeAsync(IEnumerable<TopicPartitionOffsetSpec> specs)
    {
        BeginAdminOperation();
        try
        {
            return await ListOffsetsAsync(specs, new ListOffsetsOptions { RequestTimeout = queryWatermarkTimeout })
                .ConfigureAwait(false);
        }
        finally
        {
            EndAdminOperation();
        }
    }

    private static async Task ObserveOffsetRequestsAsync(Task requests)
    {
        try
        {
            await requests.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Debug(e, "Kafka offset requests completed with an error");
        }
    }

    protected override List<Topic> FetchTopics()
    {
        var metadata = GetMetadata(queryTopicsTimeout);
        var topicError = metadata.Topics.FirstOrDefault(t => t.Error.IsError);
        if (topicError != null)
            throw new KafkaException(topicError.Error);

        var topics = metadata.Topics
            .ConvertAll(topic => new Topic(topic.Topic, topic.Partitions.Count));

        return topics;
    }

    protected override async Task GetMessagesAsync(string topicName, int partition, FetchOptions options,
        MessageStream messages, CancellationToken cancellationToken)
    {
        var tp = ValidateTopicPartition(topicName, partition);
        await GetMessagesAsync(new List<TopicPartition>() { tp }, options, messages, cancellationToken);
    }

    private TopicPartition ValidateTopicPartition(string topicName, int partition)
    {
        var topic = ValidateTopic(topicName);
        if (partition < 0 || partition >= topic.PartitionCount)
        {
            throw new ArgumentException($"Invalid partition {partition} for topic {topicName}");
        }

        return new TopicPartition(topicName, partition);
    }

    protected override async Task GetMessagesAsync(string topicName, FetchOptions options, MessageStream messages,
        CancellationToken cancellationToken)
    {
        var topic = ValidateTopic(topicName);
        var tps = topic.Partitions.Select(partition => new TopicPartition(topicName, partition.Id))
            .ToList();

        await GetMessagesAsync(tps, options, messages, cancellationToken);
    }

    public async IAsyncEnumerable<Message> StreamMessagesAsync(
        string topic,
        FetchOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var topicInfo = await Task.Run(() => ValidateTopic(topic), cancellationToken).ConfigureAwait(false);
        var tps = topicInfo.Partitions.Select(partition => new TopicPartition(topic, partition.Id)).ToList();

        await foreach (var message in StreamMessagesAsync(tps, options, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            yield return message;
        }
    }

    public async IAsyncEnumerable<Message> StreamMessagesAsync(
        string topic,
        int partition,
        FetchOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var tp = await Task.Run(() => ValidateTopicPartition(topic, partition), cancellationToken).ConfigureAwait(false);

        await foreach (var message in StreamMessagesAsync(new List<TopicPartition> { tp }, options, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            yield return message;
        }
    }

    private async IAsyncEnumerable<Message> StreamMessagesAsync(
        List<TopicPartition> tps,
        FetchOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<Message>(new BoundedChannelOptions(STREAM_CHANNEL_CAPACITY)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        var producer = Task.Run(async () =>
        {
            try
            {
                await GetMessagesAsync(tps, options, channel.Writer, cancellationToken);
                channel.Writer.TryComplete();
            }
            catch (OperationCanceledException e) when (cancellationToken.IsCancellationRequested)
            {
                channel.Writer.TryComplete(e);
            }
            catch (Exception e)
            {
                channel.Writer.TryComplete(e);
            }
        }, CancellationToken.None);

        try
        {
            await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return message;
            }

            await producer;
        }
        finally
        {
            if (!producer.IsCompleted)
            {
                channel.Writer.TryComplete();
            }
        }
    }

    private async Task GetMessagesAsync(List<TopicPartition> tps, FetchOptions options,
        MessageStream messages, CancellationToken cancellationToken)
    {
        var watermarks = await QueryWatermarkOffsetsAsync(tps, cancellationToken);
        var partitionOptions = await CreateOptionsForPartitionAsync(tps, options, cancellationToken);
        var tpos = CreateTopicPartitionOffsets(tps, watermarks, partitionOptions);

        var tpoLimits = new List<(TopicPartitionOffset Tpo, int Limit)>();
        for (var i = 0; i < tpos.Count; i++)
        {
            var tpoLimit = (Tpo: tpos[i], Limit: partitionOptions[i].Limit);
            tpoLimits.Add(tpoLimit);
        }

        await FetchMessagesAsync(tpoLimits, messages, cancellationToken);
    }

    private async Task GetMessagesAsync(
        List<TopicPartition> tps,
        FetchOptions options,
        ChannelWriter<Message> writer,
        CancellationToken cancellationToken)
    {
        var watermarks = await QueryWatermarkOffsetsAsync(tps, cancellationToken);
        var partitionOptions = await CreateOptionsForPartitionAsync(tps, options, cancellationToken);
        var tpos = CreateTopicPartitionOffsets(tps, watermarks, partitionOptions);

        var tpoLimits = new List<(TopicPartitionOffset Tpo, int Limit)>();
        for (var i = 0; i < tpos.Count; i++)
        {
            var tpoLimit = (Tpo: tpos[i], Limit: partitionOptions[i].Limit);
            tpoLimits.Add(tpoLimit);
        }

        await FetchMessagesAsync(tpoLimits, writer, cancellationToken);
    }

    private async Task FetchMessagesAsync(IReadOnlyList<(TopicPartitionOffset Tpo, int Limit)> tpoLimits,
        MessageStream messages, CancellationToken cancellationToken)
    {
        // Limit concurrency to avoid resource exhaustion
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 20,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(tpoLimits, parallelOptions, async (tpoLimit, ct) =>
        {
            await using var lease = await consumerPool.LeaseAsync(ct);
            var consumer = lease.Consumer;

            // We wrap blocking Consume calls in Task.Run to ensure we don't block the Parallel.ForEachAsync scheduler
            await Task.Run(() =>
            {
                consumer.Assign(tpoLimit.Tpo);
                try
                {
                    FetchMessages(consumer, messages, tpoLimit.Limit, ct);
                }
                finally
                {
                    consumer.Unassign();
                }
            }, ct);
        });
    }

    private async Task FetchMessagesAsync(
        IReadOnlyList<(TopicPartitionOffset Tpo, int Limit)> tpoLimits,
        ChannelWriter<Message> writer,
        CancellationToken cancellationToken)
    {
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 20,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(tpoLimits, parallelOptions, async (tpoLimit, ct) =>
        {
            await using var lease = await consumerPool.LeaseAsync(ct);
            var consumer = lease.Consumer;

            await Task.Run(async () =>
            {
                consumer.Assign(tpoLimit.Tpo);
                try
                {
                    await FetchMessagesAsync(consumer, writer, tpoLimit.Limit, ct).ConfigureAwait(false);
                }
                finally
                {
                    consumer.Unassign();
                }
            }, ct);
        });
    }

    private List<TopicPartitionOffset> CreateTopicPartitionOffsets(
        List<TopicPartition> tps, List<WatermarkOffsets> watermarks,
        List<FetchOptions> partitionOptions)
    {
        var tpos = new List<TopicPartitionOffset>();

        for (var i = 0; i < tps.Count; ++i)
        {
            WatermarkHelper.UpdateForWatermarks(partitionOptions[i], watermarks[i]);
            var tpo = new TopicPartitionOffset(tps[i], partitionOptions[i].Start.Offset);
            tpos.Add(tpo);
        }

        return tpos;
    }

    private async Task<List<FetchOptions>> CreateOptionsForPartitionAsync(List<TopicPartition> tps,
        FetchOptions options, CancellationToken cancellationToken)
    {
        var partitionOptions = new List<FetchOptions>();
        var tptList = new List<TopicPartitionTimestamp>();
        var remaining = options.Limit;
        switch (options.Start.Type)
        {
            case PositionType.Timestamp:
                var queryTimestamp = options.Start.Timestamp;
                if (options.Direction == FetchDirection.Backward)
                {
                    // Query for T + 1 to find the first message > T.
                    // This allows us to fetch messages <= T by ending just before it.
                    queryTimestamp++;
                }

                tps.ForEach(tp =>
                    tptList.Add(new(tp, new Timestamp(queryTimestamp, TimestampType.CreateTime))));

                List<TopicPartitionOffset> tpos;
                await using (var lease = await consumerPool.LeaseAsync(cancellationToken))
                {
                    tpos = await Task.Run(() => lease.Consumer.OffsetsForTimes(tptList, queryWatermarkTimeout),
                        cancellationToken).ConfigureAwait(false);
                }

                for (var i = 0; i < tpos.Count; i++)
                {
                    var limit = remaining / (tps.Count - i);
                    remaining -= limit;
                    var tpo = tpos[i];
                    var offset = tpo.Offset.Value;
                    if (offset < 0)
                    {
                        if (options.Direction == FetchDirection.Backward)
                        {
                            offset = -1; // -1 means end of partition
                            offset = offset - limit + 1;
                        }
                    }
                    else if (options.Direction == FetchDirection.Backward)
                    {
                        var desiredStart = offset - limit;
                        if (desiredStart < 0)
                        {
                            limit += (int)desiredStart;
                            offset = 0;
                        }
                        else
                        {
                            offset = desiredStart;
                        }
                    }

                    partitionOptions.Add(new(new FetchPosition(PositionType.Offset, offset), limit));
                }

                break;
            case PositionType.Offset:
                for (var i = 0; i < tps.Count; i++)
                {
                    var limit = remaining / (tps.Count - i);
                    remaining -= limit;
                    var offset = options.Start.Offset;
                    if (offset < 0)
                    {
                        offset = -1 - limit;
                    }
                    else if (options.Direction == FetchDirection.Backward)
                    {
                        var desiredStart = offset - limit + 1;
                        if (desiredStart < 0)
                        {
                            limit += (int)desiredStart;
                            offset = 0;
                        }
                        else
                        {
                            offset = desiredStart;
                        }
                    }

                    partitionOptions.Add(new(new(PositionType.Offset, offset), limit));
                }

                break;
            default:
                break;
        }

        return partitionOptions;
    }

    private int FetchMessages(IConsumer<byte[], byte[]> consumer, MessageStream messages, int requiredCount,
        CancellationToken cancellationToken)
    {
        if (requiredCount <= 0)
        {
            return 0;
        }

        var batch = new List<Message>(100);
        var lastFlushTime = DateTime.Now;
        var batchInterval = TimeSpan.FromMilliseconds(100);
        var consecutiveEmptyPolls = 0;
        // Increase max consecutive empty polls because EnablePartitionEof is true.
        // It will receive an EOF immediately if there are no messages.
        // Null returns mean the consumer is still connecting or waiting for metadata.
        var maxEmptyPolls = 10;

        // No lock needed: the consumer is exclusively leased from ConsumerPool,
        // ensuring only one task accesses it at a time.
        while (requiredCount > 0 && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(consumeTimeout);
                if (result == null)
                {
                    consecutiveEmptyPolls++;
                    Log.Debug("Waiting for consumer connection/metadata (poll timeout {Current}/{Max})",
                        consecutiveEmptyPolls, maxEmptyPolls);
                    if (consecutiveEmptyPolls >= maxEmptyPolls)
                    {
                        Log.Information("Stopping fetch after {Count} consecutive empty polls (connection timeout)",
                            consecutiveEmptyPolls);
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new TimeoutException("Timed out waiting for a response from the Kafka consumer.");
                    }

                    continue;
                }

                consecutiveEmptyPolls = 0;

                if (result.IsPartitionEOF)
                {
                    Log.Information("End of partition reached");
                    break;
                }

                var message = MessageConverter.CreateMessage(result);
                batch.Add(message);
                --requiredCount;

                if (batch.Count >= 100 || DateTime.Now - lastFlushTime >= batchInterval)
                {
                    FlushBatch(messages, batch);
                    lastFlushTime = DateTime.Now;
                }
            }
            catch (ConsumeException e)
            {
                FlushBatch(messages, batch);
                Log.Error(e, "Error while consuming message");
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
            catch (Exception e)
            {
                FlushBatch(messages, batch);
                Log.Error(e, "Error while consuming message");
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
        }

        FlushBatch(messages, batch);
        cancellationToken.ThrowIfCancellationRequested();
        return requiredCount;
    }

    private async Task<int> FetchMessagesAsync(
        IConsumer<byte[], byte[]> consumer,
        ChannelWriter<Message> writer,
        int requiredCount,
        CancellationToken cancellationToken)
    {
        if (requiredCount <= 0)
        {
            return 0;
        }

        var consecutiveEmptyPolls = 0;
        var maxEmptyPolls = 10;

        while (requiredCount > 0 && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(consumeTimeout);
                if (result == null)
                {
                    consecutiveEmptyPolls++;
                    Log.Debug("Waiting for consumer connection/metadata (poll timeout {Current}/{Max})",
                        consecutiveEmptyPolls, maxEmptyPolls);
                    if (consecutiveEmptyPolls >= maxEmptyPolls)
                    {
                        Log.Information("Stopping fetch after {Count} consecutive empty polls (connection timeout)",
                            consecutiveEmptyPolls);
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new TimeoutException("Timed out waiting for a response from the Kafka consumer.");
                    }

                    continue;
                }

                consecutiveEmptyPolls = 0;

                if (result.IsPartitionEOF)
                {
                    Log.Information("End of partition reached");
                    break;
                }

                var message = MessageConverter.CreateMessage(result);
                await writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
                --requiredCount;
            }
            catch (ConsumeException e)
            {
                Log.Error(e, "Error while consuming message");
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while consuming message");
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return requiredCount;
    }

    private void FlushBatch(MessageStream messages, List<Message> batch)
    {
        if (batch.Count > 0)
        {
            lock (messages.Messages)
            {
                messages.Messages.AddRange(batch);
            }

            batch.Clear();
        }
    }

    private async Task<List<WatermarkOffsets>> QueryWatermarkOffsetsAsync(List<TopicPartition> tps,
        CancellationToken cancellationToken)
    {
        Log.Debug("Querying watermark offsets for {TopicPartitions}", tps);
        if (tps.Count == 0)
        {
            return new List<WatermarkOffsets>();
        }

        var earliestSpecs = tps.Select(tp => new TopicPartitionOffsetSpec
            { TopicPartition = tp, OffsetSpec = OffsetSpec.Earliest() }).ToList();
        var latestSpecs = tps.Select(tp => new TopicPartitionOffsetSpec
            { TopicPartition = tp, OffsetSpec = OffsetSpec.Latest() }).ToList();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var earliestTask = ListOffsetsWithLifetimeAsync(earliestSpecs);
            var latestTask = ListOffsetsWithLifetimeAsync(latestSpecs);
            var requests = Task.WhenAll(earliestTask, latestTask);
            _ = ObserveOffsetRequestsAsync(requests);

            await requests.WaitAsync(queryWatermarkTimeout, cancellationToken).ConfigureAwait(false);

            var earliestResults = await earliestTask;
            var latestResults = await latestTask;

            return BuildWatermarkOffsets(tps, earliestResults.ResultInfos, latestResults.ResultInfos);
        }
        catch (Exception e)
        {
            Log.Error(e, "Error querying watermark offsets");
            throw;
        }
    }

    private List<WatermarkOffsets> BuildWatermarkOffsets(List<TopicPartition> tps, IReadOnlyList<ListOffsetsResultInfo> earliestInfos, IReadOnlyList<ListOffsetsResultInfo> latestInfos)
    {
        string? singleTopic = tps.Count > 0 ? tps[0].Topic : null;
        bool isSingleTopic = true;
        int maxPartition = -1;

        foreach (var tp in tps)
        {
            if (tp.Topic != singleTopic)
            {
                isSingleTopic = false;
                break;
            }
            if (tp.Partition.Value > maxPartition)
            {
                maxPartition = tp.Partition.Value;
            }
        }

        if (isSingleTopic && maxPartition is >= 0 and < MAX_PARTITION_COUNT)
        {
            return BuildWatermarkOffsetsSingleTopic(tps, earliestInfos, latestInfos, maxPartition);
        }

        return BuildWatermarkOffsetsMultiTopic(tps, earliestInfos, latestInfos);
    }

    private List<WatermarkOffsets> BuildWatermarkOffsetsSingleTopic(List<TopicPartition> tps, IReadOnlyList<ListOffsetsResultInfo> earliestInfos, IReadOnlyList<ListOffsetsResultInfo> latestInfos, int maxPartition)
    {
        var arr = new (long Low, long High)[maxPartition + 1];
        for (int i = 0; i < arr.Length; i++)
        {
            arr[i] = (-1, -1);
        }

        foreach (var info in earliestInfos)
        {
            var tpoe = info.TopicPartitionOffsetError;
            if (tpoe.Error.IsError) throw new KafkaException(tpoe.Error);
            var p = tpoe.TopicPartition.Partition.Value;
            if (p <= maxPartition) arr[p].Low = tpoe.Offset.Value;
        }

        foreach (var info in latestInfos)
        {
            var tpoe = info.TopicPartitionOffsetError;
            if (tpoe.Error.IsError) throw new KafkaException(tpoe.Error);
            var p = tpoe.TopicPartition.Partition.Value;
            if (p <= maxPartition) arr[p].High = tpoe.Offset.Value;
        }

        var output = new List<WatermarkOffsets>(tps.Count);
        foreach (var tp in tps)
        {
            var p = tp.Partition.Value;
            var low = arr[p].Low;
            var high = arr[p].High;
            if (low != -1 && high != -1)
            {
                output.Add(new WatermarkOffsets(new Offset(low), new Offset(high)));
            }
            else
            {
                throw new Exception($"Failed to get watermark offsets for {tp}");
            }
        }
        return output;
    }

    private List<WatermarkOffsets> BuildWatermarkOffsetsMultiTopic(List<TopicPartition> tps, IReadOnlyList<ListOffsetsResultInfo> earliestInfos, IReadOnlyList<ListOffsetsResultInfo> latestInfos)
    {
        var resultsMap = new Dictionary<TopicPartition, (long Low, long High)>(tps.Count);

        foreach (var info in earliestInfos)
        {
            var tpoe = info.TopicPartitionOffsetError;
            if (tpoe.Error.IsError)
            {
                throw new KafkaException(tpoe.Error);
            }
            resultsMap[tpoe.TopicPartition] = (tpoe.Offset.Value, -1);
        }

        foreach (var info in latestInfos)
        {
            var tpoe = info.TopicPartitionOffsetError;
            if (tpoe.Error.IsError)
            {
                throw new KafkaException(tpoe.Error);
            }
            if (resultsMap.TryGetValue(tpoe.TopicPartition, out var val))
            {
                resultsMap[tpoe.TopicPartition] = (val.Low, tpoe.Offset.Value);
            }
        }

        return tps.ConvertAll(tp =>
        {
            if (resultsMap.TryGetValue(tp, out var val))
            {
                return new WatermarkOffsets(new Offset(val.Low), new Offset(val.High));
            }
            throw new Exception($"Failed to get watermark offsets for {tp}");
        });
    }

    #endregion Read

    #region IDisposable implemenatation

    public override void Dispose()
    {
        bool disposeAdmin;
        lock (adminLifetimeGate)
        {
            if (disposed != 0)
                return;
            disposed = 1;
            disposeAdmin = activeAdminOperations == 0;
        }
        try
        {
            consumerPool.Dispose();
        }
        finally
        {
            if (disposeAdmin)
                DisposeAdminClient();
            base.Dispose();
        }
    }

    private void DisposeAdminClient()
    {
        _ = Task.Run(() =>
        {
            try
            {
                AdminClient.Dispose();
            }
            catch (Exception e)
            {
                Log.Warning(e, "Failed to dispose Kafka admin client");
            }
        });
    }

    #endregion IDisposable implemenatation
}