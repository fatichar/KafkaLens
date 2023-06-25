using System.Net.NetworkInformation;
using Confluent.Kafka;
using KafkaLens.Shared.Models;
using Serilog;
using TopicPartition = Confluent.Kafka.TopicPartition;

namespace KafkaLens.Core.Services;

class ConfluentConsumer : ConsumerBase, IDisposable
{
    private readonly TimeSpan queryWatermarkTimeout = TimeSpan.FromSeconds(5);
    private readonly TimeSpan queryTopicsTimeout = TimeSpan.FromSeconds(5);
    private readonly TimeSpan consumeTimeout = TimeSpan.FromSeconds(5);

    private IConsumer<byte[], byte[]> Consumer { get; }

    private ConsumerConfig Config { get; set; }

    private IAdminClient AdminClient { get; }

    #region Create
    internal ConfluentConsumer(string url)
    {
        Config = CreateConsumerConfig(url);
        AdminClient = CreateAdminClient(Config.BootstrapServers);
        Consumer = CreateConsumer();
    }

    private IConsumer<byte[], byte[]> CreateConsumer()
    {
        return new ConsumerBuilder<byte[], byte[]>(Config).Build();
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
            StatisticsIntervalMs = 30_000
        };
    }

    private static IAdminClient CreateAdminClient(string url)
    {
        var config = new AdminClientConfig
        {
            BootstrapServers = url
        };
        return new AdminClientBuilder(config).Build();
    }
    #endregion Create

    #region Read

    protected override List<Topic> FetchTopics()
    {
        var metadata = AdminClient.GetMetadata(queryTopicsTimeout);

        var topics = metadata.Topics
            .ConvertAll(topic => new Topic(topic.Topic, topic.Partitions.Count));

        return topics;
    }

    protected override void GetMessages(string topicName, int partition, FetchOptions options,
        MessageStream messages)
    {
        Log.Information("Fetching {MessageCount} messages for partition {Topic}:{Partition}", options.Limit, topicName, partition);
        var tp = ValidateTopicPartition(topicName, partition);
        GetMessages(new List<Confluent.Kafka.TopicPartition>(){tp}, options, messages);
        Log.Information("Fetched {MessageCount} messages for partition {Topic}:{Partition}", messages.Messages.Count, topicName, partition);
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

    protected override void GetMessages(string topicName, FetchOptions options, MessageStream messages)
    {
        Log.Information("Fetching {MessageCount} messages for topic {Topic}", options.Limit, topicName);

        ValidateTopic(topicName);
        var topic = Topics[topicName];
        var tps = topic.Partitions.Select(partition => new Confluent.Kafka.TopicPartition(topicName, partition.Id)).ToList();

        GetMessages(tps, options, messages);
        Log.Information("Fetched {MessageCount} messages for topic {Topic}", messages.Messages.Count, topicName);
    }

    private void GetMessages(List<TopicPartition> tps, FetchOptions options,
        MessageStream messages)
    {
        var watermarks = QueryWatermarkOffsets(tps);
        var partitionOptions = CreateOptionsForPartition(tps, options);
        var tpos = CreateTopicPartitionOffsets(tps, watermarks, partitionOptions);

        var tpoLimits = new List<(TopicPartitionOffset Tpo, int Limit)>();
        for (var i = 0; i < tpos.Count; i++)
        {
            var tpoLimit = (Tpo: tpos[i], Limit: partitionOptions[i].Limit);
            tpoLimits.Add(tpoLimit);
        }
        FetchMessages(tpoLimits, messages);
    }

    private void FetchMessages(IReadOnlyList<(TopicPartitionOffset Tpo, int Limit)> tpoLimits,
        MessageStream messages)
    {
        // var messages = new ConcurrentBag<Message>();
        if (tpoLimits.Count == 1)
        {
            Consumer.Assign(tpoLimits[0].Tpo);
            FetchMessages(Consumer, messages, tpoLimits[0].Limit);
            Consumer.Unassign();
        }
        else
        {
            tpoLimits.AsParallel().ForAll(tpoLimit =>
            {
                using var consumer = CreateConsumer();
                consumer.Assign(tpoLimit.Tpo);
                FetchMessages(consumer, messages, tpoLimit.Limit);
            });
        }

        messages.HasMore = false;
    }

    private List<Confluent.Kafka.TopicPartitionOffset> CreateTopicPartitionOffsets(List<Confluent.Kafka.TopicPartition> tps, List<WatermarkOffsets> watermarks, List<FetchOptions> partitionOptions)
    {
        var tpos = new List<Confluent.Kafka.TopicPartitionOffset>();

        for (var i = 0; i < tps.Count; ++i)
        {
            UpdateForWatermarks(partitionOptions[i], watermarks[i]);
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
                        offset = -1 -limit;
                    }
                    partitionOptions.Add(new(new(PositionType.OFFSET, offset), limit));
                }
                break;
            default:
                break;
        }
        return partitionOptions;
    }

    private int FetchMessages(IConsumer<byte[], byte[]> consumer, MessageStream messages, int requiredCount)
    {
        if (requiredCount <= 0)
        {
            return 0;
        }
        lock (Consumer)
        {
            while (requiredCount > 0)
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

                    messages.Messages.Add(CreateMessage(result));
                    --requiredCount;
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
                finally
                {
                    
                }
            }
        }

        return requiredCount;
    }

    private List<WatermarkOffsets> QueryWatermarkOffsets(List<Confluent.Kafka.TopicPartition> tps)
    {
        lock(Consumer)
        {
            Log.Debug("Querying watermark offsets for {TopicPartitions}", tps);
            return tps.ConvertAll(tp => Consumer.QueryWatermarkOffsets(tp, queryWatermarkTimeout));
        }
    }

    private static void UpdateForWatermarks(FetchOptions fetchOptions, WatermarkOffsets watermarks)
    {
        var position = fetchOptions.Start;
        var offset = position.Offset;
        if (offset < 0)
        {
            // if options.Start.Offset = -1 => offset = watermarks.High
            // means no message will be returned
            offset = watermarks.High.Value + 1 + offset;
        }
        if (offset < watermarks.Low.Value)
        {
            offset = watermarks.Low.Value;
        }
        else if (offset > watermarks.High.Value)
        {
            offset = watermarks.High.Value;
        }
        position.SetOffset(offset);
        if (position.Offset + fetchOptions.Limit > watermarks.High.Value)
        {
            fetchOptions.Limit = (int)(watermarks.High.Value - position.Offset);
        }
    }

    private static Message CreateMessage(ConsumeResult<byte[], byte[]> result)
    {
        var epochMillis = result.Message.Timestamp.UnixTimestampMs;
        var headers = result.Message.Headers.ToDictionary(header =>
            header.Key, header => header.GetValueBytes());

        return new Message(epochMillis, headers, result.Message.Key, result.Message.Value)
        {
            Partition = result.Partition.Value,
            Offset = result.Offset.Value
        };
    }
    #endregion Read

    #region IDisposable implemenatation
    public void Dispose()
    {
        Consumer.Dispose();
    }
    #endregion IDisposable implemenatation
}