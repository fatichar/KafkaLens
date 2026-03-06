using System.Collections.Concurrent;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using KafkaLens.Core.Utils;
using KafkaLens.Shared.Models;
using Serilog;
using TopicPartition = Confluent.Kafka.TopicPartition;

namespace KafkaLens.Core.Services;

internal class ConfluentConsumer : ConsumerBase, IDisposable
{
    private readonly TimeSpan queryWatermarkTimeout;
    private readonly TimeSpan queryTopicsTimeout;
    private readonly TimeSpan consumeTimeout;
    private const int MaxConsecutiveEmptyPolls = 3;
    private readonly KafkaConfig kafkaConfig;

    private readonly ConsumerPool consumerPool;
    protected ConsumerConfig Config { get; set; }

    protected IAdminClient AdminClient { get; }

    private class ConsumerPool : IDisposable
    {
        private readonly Func<IConsumer<byte[], byte[]>> factory;
        private readonly ConcurrentBag<IConsumer<byte[], byte[]>> pool = new();
        private readonly SemaphoreSlim semaphore;

        public ConsumerPool(int maxLimit, Func<IConsumer<byte[], byte[]>> factory)
        {
            this.factory = factory;
            semaphore = new SemaphoreSlim(maxLimit, maxLimit);

            // Warm-up: Pre-create one consumer so the very first fetch doesn't pay the initialization penalty.
            // This happens in the background.
            Task.Run(() =>
            {
                try
                {
                    var consumer = factory();
                    pool.Add(consumer);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Failed to warm up consumer pool");
                }
            });
        }

        public async Task<ConsumerLease> LeaseAsync(CancellationToken ct)
        {
            await semaphore.WaitAsync(ct);
            if (!pool.TryTake(out var consumer))
            {
                consumer = factory();
            }

            return new ConsumerLease(consumer, this);
        }

        public void Return(IConsumer<byte[], byte[]> consumer)
        {
            pool.Add(consumer);
            semaphore.Release();
        }

        public void Dispose()
        {
            foreach (var c in pool)
            {
                c.Dispose();
            }

            pool.Clear();
            semaphore.Dispose();
        }
    }

    private class ConsumerLease : IAsyncDisposable, IDisposable
    {
        public IConsumer<byte[], byte[]> Consumer { get; }
        private readonly ConsumerPool pool;

        public ConsumerLease(IConsumer<byte[], byte[]> consumer, ConsumerPool pool)
        {
            Consumer = consumer;
            this.pool = pool;
        }

        public ValueTask DisposeAsync()
        {
            pool.Return(Consumer);
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            pool.Return(Consumer);
        }
    }

    #region Create

    internal ConfluentConsumer(string url, KafkaConfig kafkaConfig)
    {
        this.kafkaConfig = kafkaConfig;
        queryWatermarkTimeout = TimeSpan.FromMilliseconds(kafkaConfig.QueryWatermarkTimeoutMs);
        queryTopicsTimeout = TimeSpan.FromMilliseconds(kafkaConfig.QueryTopicsTimeoutMs);
        consumeTimeout = TimeSpan.FromMilliseconds(kafkaConfig.ConsumeTimeoutMs);

        Config = CreateConsumerConfig(url);
        Config.Set("log_level", "0");
        AdminClient = CreateAdminClient(Config.BootstrapServers);
        consumerPool = new ConsumerPool(10, CreateConsumer); // Max limit of 10 concurrent consumers
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
        try
        {
            var metadata = AdminClient.GetMetadata(TimeSpan.FromMilliseconds(kafkaConfig.AdminMetadataTimeoutMs));
            return metadata.OriginatingBrokerId != -1;
        }
        catch (Exception e)
        {
            // Logging only the message to avoid stack trace clutter for expected timeouts
            Log.Debug("Connection validation failed: {Message}", e.Message);
            return false;
        }
    }

    protected override List<Topic> FetchTopics()
    {
        var metadata = AdminClient.GetMetadata(queryTopicsTimeout);

        var topics = metadata.Topics
            .ConvertAll(topic => new Topic(topic.Topic, topic.Partitions.Count));

        return topics;
    }

    protected override async Task GetMessagesAsync(string topicName, int partition, FetchOptions options,
        MessageStream messages, CancellationToken cancellationToken)
    {
        var tp = ValidateTopicPartition(topicName, partition);
        await GetMessagesAsync(new List<Confluent.Kafka.TopicPartition>() { tp }, options, messages, cancellationToken);
    }

    private Confluent.Kafka.TopicPartition ValidateTopicPartition(string topicName, int partition)
    {
        var topic = ValidateTopic(topicName);
        if (partition < 0 || partition >= topic.PartitionCount)
        {
            throw new ArgumentException($"Invalid partition {partition} for topic {topicName}");
        }

        return new Confluent.Kafka.TopicPartition(topicName, partition);
    }

    protected override async Task GetMessagesAsync(string topicName, FetchOptions options, MessageStream messages,
        CancellationToken cancellationToken)
    {
        var topic = ValidateTopic(topicName);
        var tps = topic.Partitions.Select(partition => new Confluent.Kafka.TopicPartition(topicName, partition.Id))
            .ToList();

        await GetMessagesAsync(tps, options, messages, cancellationToken);
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
                FetchMessages(consumer, messages, tpoLimit.Limit, ct);
                consumer.Unassign();
            }, ct);
        });
    }

    private List<Confluent.Kafka.TopicPartitionOffset> CreateTopicPartitionOffsets(
        List<Confluent.Kafka.TopicPartition> tps, List<WatermarkOffsets> watermarks,
        List<FetchOptions> partitionOptions)
    {
        var tpos = new List<Confluent.Kafka.TopicPartitionOffset>();

        for (var i = 0; i < tps.Count; ++i)
        {
            WatermarkHelper.UpdateForWatermarks(partitionOptions[i], watermarks[i]);
            var tpo = new TopicPartitionOffset(tps[i], partitionOptions[i].Start.Offset);
            tpos.Add(tpo);
        }

        return tpos;
    }

    private async Task<List<FetchOptions>> CreateOptionsForPartitionAsync(List<Confluent.Kafka.TopicPartition> tps,
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
                    tpos = lease.Consumer.OffsetsForTimes(tptList, queryWatermarkTimeout);
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

        lock (consumer)
        {
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
                            break;
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
                    Log.Error(e, "Error while consuming message");
                    break;
                }
                catch (Exception e)
                {
                    Log.Error(e, "Error while consuming message");
                    break;
                }
            }
        }

        FlushBatch(messages, batch);
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

    private async Task<List<WatermarkOffsets>> QueryWatermarkOffsetsAsync(List<Confluent.Kafka.TopicPartition> tps,
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
            var earliestTask = ListOffsetsAsync(earliestSpecs, new ListOffsetsOptions());
            var latestTask = ListOffsetsAsync(latestSpecs, new ListOffsetsOptions());

            await Task.WhenAll(earliestTask, latestTask).WaitAsync(queryWatermarkTimeout, cancellationToken);

            var earliestResults = await earliestTask;
            var latestResults = await latestTask;

            var resultsMap = new Dictionary<Confluent.Kafka.TopicPartition, (long Low, long High)>();

            foreach (var info in earliestResults.ResultInfos)
            {
                var tpoe = info.TopicPartitionOffsetError;
                if (tpoe.Error.IsError)
                {
                    throw new KafkaException(tpoe.Error);
                }

                resultsMap[tpoe.TopicPartition] = (tpoe.Offset.Value, -1);
            }

            foreach (var info in latestResults.ResultInfos)
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
        catch (Exception e)
        {
            Log.Error(e, "Error querying watermark offsets");
            throw;
        }
    }

    #endregion Read

    #region IDisposable implemenatation

    public override void Dispose()
    {
        consumerPool.Dispose();
        AdminClient.Dispose();
        base.Dispose();
    }

    #endregion IDisposable implemenatation
}