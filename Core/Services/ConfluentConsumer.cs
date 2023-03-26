using System.Net.NetworkInformation;
using Confluent.Kafka;
using KafkaLens.Shared.Models;
using Serilog;
using TopicPartition = Confluent.Kafka.TopicPartition;

namespace KafkaLens.Core.Services;

class ConfluentConsumer : IKafkaConsumer, IDisposable
{
    private readonly TimeSpan queryWatermarkTimeout = TimeSpan.FromSeconds(5);
    private readonly TimeSpan queryTopicsTimeout = TimeSpan.FromSeconds(5);
    private readonly TimeSpan consumeTimeout = TimeSpan.FromSeconds(5);

    private Dictionary<string, Topic> Topics { get; set; } = new();

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
    public List<Topic> GetTopics()
    {
        if (Topics.Count == 0)
        {
            try
            {
                LoadTopics();
            }
            catch (KafkaException e)
            {
                Console.WriteLine(e);
                throw new Exception("Failed to load topics", e);
            }
        }
        return Topics.Values.ToList();
    }

    private void LoadTopics()
    {
        Log.Information("Loading topics...");
        var topics = FetchTopics();
        topics.ForEach(topic => Topics.Add(topic.Name, topic));
        Log.Information("Loaded {TopicsCount} topics", topics.Count);
    }

    private List<Topic> FetchTopics()
    {
        var metadata = AdminClient.GetMetadata(queryTopicsTimeout);

        var topics = metadata.Topics
            .ConvertAll(topic => new Topic(topic.Topic, topic.Partitions.Count));
        
        // var topicPartitions = topics
        //     .SelectMany(topic => topic.Partitions.Select(partition => new TopicPartition(topic.Name, partition.Id)))
        //     .ToList();
        //
        // QueryWatermarkOffsets(topicPartitions);

        return topics;
    }

    public MessageStream GetMessageStream(string topic, int partition, FetchOptions options)
    {
        var messages = new MessageStream();
        Task.Run(() =>
            GetMessages(topic, partition, options, messages));
        return messages;
    }

    public async Task<List<Message>> GetMessagesAsync(string topic, int partition, FetchOptions options)
    {
        var messages = new MessageStream();
        await Task.Run(() => GetMessages(topic, partition, options, messages));
        return messages.Messages.ToList();
    }

    private void GetMessages(string topicName, int partition, FetchOptions options,
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

    public MessageStream GetMessageStream(string topic, FetchOptions options)
    {
        var messages = new MessageStream();
        Task.Run(() => GetMessages(topic, options, messages));
        return messages;
    }

    public async Task<List<Message>> GetMessagesAsync(string topic, FetchOptions options)
    {
        var messages = new MessageStream();
        await Task.Run(() => GetMessages(topic, options, messages));
        return messages.Messages.ToList();
    }

    public void GetMessages(string topicName, FetchOptions options, MessageStream messages)
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
            UpdateForWatermarks(partitionOptions[i].Start, watermarks[i]);
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
        lock (Consumer)
        {
            while (requiredCount > 0)
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

    private Topic ValidateTopic(string topicName)
    {
        if (Topics.Count == 0)
        {
            LoadTopics();
        }
        if (Topics.TryGetValue(topicName, out var topic))
        {
            return topic;
        }
        throw new Exception($"Topic {topicName} does not exist.");
    }

    private TopicPartitionOffset CreateTopicPartitionOffset(Confluent.Kafka.TopicPartition tp, WatermarkOffsets watermarks, FetchOptions options)
    {
        UpdateForTimestamps(tp, options);

        UpdateForWatermarks(options.Start, watermarks);
        if (options.End == null)
        {
            options.End = new FetchPosition(PositionType.OFFSET, options.Start.Offset + options.Limit);
        }
        UpdateForWatermarks(options.End, watermarks);
        return new TopicPartitionOffset(tp, options.Start.Offset);
    }

    private void UpdateForTimestamps(Confluent.Kafka.TopicPartition tp, FetchOptions options)
    {
        var tptList = new List<TopicPartitionTimestamp>();
        if (options.Start.Type == PositionType.TIMESTAMP)
        {
            tptList.Add(new(tp, new Timestamp(options.Start.Timestamp, TimestampType.CreateTime)));
        }
        if (options.End?.Type == PositionType.TIMESTAMP)
        {
            tptList.Add(new(tp, new Timestamp(options.End.Timestamp, TimestampType.CreateTime)));
        }
        if (tptList.Count > 0)
        {
            var tops = Consumer.OffsetsForTimes(tptList, queryWatermarkTimeout);
            var i = 0;
            if (options.Start.Type == PositionType.TIMESTAMP)
            {
                options.Start.SetOffset(tops[i++].Offset);
            }
            if (options.End?.Type == PositionType.TIMESTAMP)
            {
                options.End.SetOffset(tops[i++].Offset);
            }
        }
    }

    private static void UpdateForWatermarks(FetchPosition position, WatermarkOffsets watermarks)
    {
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
    }

    private void UpdateLimitUsingEnd(FetchOptions options)
    {
        if (options.End != null)
        {
            var end = options.End;

            // now end has a valid offset
            var distance = (int)(end.Offset - options.Start.Offset);
            if (options.Limit == 0 || options.Limit > distance)
            {
                options.Limit = distance;
            }
            else
            {
                end.SetOffset(options.Start.Offset + distance);
            }
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