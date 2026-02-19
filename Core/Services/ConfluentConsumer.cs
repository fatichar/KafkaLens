using Confluent.Kafka;
using Confluent.Kafka.Admin;
using KafkaLens.Core.Utils;
using KafkaLens.Shared.Models;
using Serilog;
using TopicPartition = Confluent.Kafka.TopicPartition;

namespace KafkaLens.Core.Services;

class ConfluentConsumer : ConsumerBase, IDisposable
{
    private readonly TimeSpan queryWatermarkTimeout = TimeSpan.FromSeconds(10);
    private readonly TimeSpan queryTopicsTimeout = TimeSpan.FromSeconds(5);
    private readonly TimeSpan consumeTimeout = TimeSpan.FromSeconds(5);

    protected IConsumer<byte[], byte[]> Consumer { get; }

    protected ConsumerConfig Config { get; set; }

    protected IAdminClient AdminClient { get; }

    #region Create

    internal ConfluentConsumer(string url)
    {
        Config = CreateConsumerConfig(url);
        Config.Set("log_level", "0");
        AdminClient = CreateAdminClient(Config.BootstrapServers);
        Consumer = CreateConsumer();
    }

    protected virtual IConsumer<byte[], byte[]> CreateConsumer()
    {
        return new ConsumerBuilder<byte[], byte[]>(Config)
            .SetLogHandler((c, m) => { })
            .SetErrorHandler((c, e) => { })
            .Build();
    }

    private static ConsumerConfig CreateConsumerConfig(String url)
    {
        return new ConsumerConfig
        {
            GroupId = "KafkaLens.Server",
            ClientId = "KafkaLens.Server",
            BootstrapServers = url,
            EnableAutoOffsetStore = false,
            EnableAutoCommit = false,
            FetchMaxBytes = 2_000_000,
            StatisticsIntervalMs = 30_000,
            LogQueue = true
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

    protected virtual Task<ListOffsetsResult> ListOffsetsAsync(IEnumerable<TopicPartitionOffsetSpec> topicPartitionOffsets, ListOffsetsOptions options)
    {
        return AdminClient.ListOffsetsAsync(topicPartitionOffsets, options);
    }

    #endregion Create

    #region Read

    public override bool ValidateConnection()
    {
        try
        {
            var metadata = AdminClient.GetMetadata(TimeSpan.FromSeconds(3));
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
        ValidateTopic(topicName);
        var topic = Topics[topicName];
        var tps = topic.Partitions.Select(partition => new Confluent.Kafka.TopicPartition(topicName, partition.Id))
            .ToList();

        await GetMessagesAsync(tps, options, messages, cancellationToken);
    }

    private async Task GetMessagesAsync(List<TopicPartition> tps, FetchOptions options,
        MessageStream messages, CancellationToken cancellationToken)
    {
        var watermarks = await QueryWatermarkOffsetsAsync(tps, cancellationToken);
        var partitionOptions = CreateOptionsForPartition(tps, options);
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
        if (tpoLimits.Count == 1)
        {
            lock (Consumer)
            {
                Consumer.Assign(tpoLimits[0].Tpo);
                FetchMessages(Consumer, messages, tpoLimits[0].Limit, cancellationToken);
                Consumer.Unassign();
            }
        }
        else
        {
            // Limit concurrency to avoid resource exhaustion (e.g., too many open connections/files)
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 20,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(tpoLimits, parallelOptions, async (tpoLimit, ct) =>
            {
                // We wrap blocking Consume calls in Task.Run to ensure we don't block the Parallel.ForEachAsync scheduler
                await Task.Run(() =>
                {
                    using var consumer = CreateConsumer();
                    consumer.Assign(tpoLimit.Tpo);
                    FetchMessages(consumer, messages, tpoLimit.Limit, ct);
                }, ct);
            });
        }
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

    private List<FetchOptions> CreateOptionsForPartition(List<Confluent.Kafka.TopicPartition> tps, FetchOptions options)
    {
        var partitionOptions = new List<FetchOptions>();
        var tptList = new List<TopicPartitionTimestamp>();
        var remaining = options.Limit;
        switch (options.Start.Type)
        {
            case PositionType.TIMESTAMP:
                tps.ForEach(tp =>
                    tptList.Add(new(tp, new Timestamp(options.Start.Timestamp, TimestampType.CreateTime))));

                var tpos = Consumer.OffsetsForTimes(tptList, queryWatermarkTimeout);

                for (var i = 0; i < tpos.Count; i++)
                {
                    var limit = remaining / (tps.Count - i);
                    remaining -= limit;
                    var tpo = tpos[i];
                    partitionOptions.Add(new(new FetchPosition(PositionType.OFFSET, tpo.Offset.Value), limit));
                }

                break;
            case PositionType.OFFSET:
                for (var i = 0; i < tps.Count; i++)
                {
                    var limit = remaining / (tps.Count - i);
                    remaining -= limit;
                    var offset = options.Start.Offset;
                    if (offset < 0)
                    {
                        offset = -1 - limit;
                    }

                    partitionOptions.Add(new(new(PositionType.OFFSET, offset), limit));
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

        lock (consumer)
        {
            while (requiredCount > 0 && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(consumeTimeout);
                    if (result == null)
                    {
                        Log.Information("Got null message. Must be timed out");
                        break;
                    }

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

    private async Task<List<WatermarkOffsets>> QueryWatermarkOffsetsAsync(List<Confluent.Kafka.TopicPartition> tps, CancellationToken cancellationToken)
    {
        Log.Debug("Querying watermark offsets for {TopicPartitions}", tps);
        if (tps.Count == 0)
        {
            return new List<WatermarkOffsets>();
        }

        var earliestSpecs = tps.Select(tp => new TopicPartitionOffsetSpec { TopicPartition = tp, OffsetSpec = OffsetSpec.Earliest() }).ToList();
        var latestSpecs = tps.Select(tp => new TopicPartitionOffsetSpec { TopicPartition = tp, OffsetSpec = OffsetSpec.Latest() }).ToList();

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
        lock (Consumer)
        {
            Consumer.Dispose();
        }
        AdminClient.Dispose();
        base.Dispose();
    }

    #endregion IDisposable implemenatation
}