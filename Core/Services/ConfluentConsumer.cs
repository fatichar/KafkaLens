using System.Diagnostics;
using Confluent.Kafka;
using KafkaLens.Shared.Models;

namespace KafkaLens.Core.Services
{
    class ConfluentConsumer : IKafkaConsumer, IDisposable
    {
        private readonly TimeSpan queryWatermarkTimeout = TimeSpan.FromSeconds(5);
        private readonly TimeSpan queryTopicsTimeout = TimeSpan.FromSeconds(5);
        private readonly TimeSpan consumeTimeout = TimeSpan.FromSeconds(10);

        public Dictionary<string, Topic> Topics { get; private set; } = new Dictionary<string, Topic>();

        private IConsumer<byte[], byte[]> Consumer { get; }

        public ConsumerConfig Config { get; private set; }

        private IAdminClient AdminClient { get; }

        #region Create
        internal ConfluentConsumer(string url)
        {
            Config = CreateConsumerConfig(url);
            AdminClient = CreateAdminClient(Config.BootstrapServers);
            Consumer = new ConsumerBuilder<byte[], byte[]>(Config).Build();
        }

        private static ConsumerConfig CreateConsumerConfig(String url)
        {
            return new ConsumerConfig
            {
                GroupId = "KafkaLens.Server",
                ClientId = "KafkaLens.Server",
                BootstrapServers = url,
                EnableAutoOffsetStore = false,
                EnableAutoCommit = false
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
                LoadTopics();
            }
            return Topics.Values.ToList();
        }

        private void LoadTopics()
        {
            Console.WriteLine("Loading topics...");
            var topics = FetchTopics();
            topics.ForEach(topic => Topics.Add(topic.Name, topic));
        }

        private List<Topic> FetchTopics()
        {
            var metadata = AdminClient.GetMetadata(queryTopicsTimeout);

            var topics = metadata.Topics
                .ConvertAll(topic => new Topic(topic.Topic, topic.Partitions.Count));

            return topics;
        }

        public async Task<List<Message>> GetMessagesAsync(string topic, int partition, FetchOptions options)
        {
            return await Task.Run(() =>
                GetMessages(topic, partition, options));
        }

        public List<Message> GetMessages(string topicName, int partition, FetchOptions options)
        {
            Console.WriteLine($"Getting messages for {topicName}:{partition}");
            var watch = new Stopwatch();
            watch.Start();
            var tp = ValidateTopicPartition(topicName, partition);
            var messages = new List<Message>();
            lock(Consumer)
            {
                Console.WriteLine($"Got lock in {watch.ElapsedMilliseconds} ms");
                watch.Restart();
                Console.WriteLine($"assigned tp in {watch.ElapsedMilliseconds} ms");
                watch.Restart();
                var watermarks = Consumer.QueryWatermarkOffsets(tp, queryWatermarkTimeout);
                Console.WriteLine($"Got watermarks in {watch.ElapsedMilliseconds} ms");
                watch.Restart();
                var tpo = CreateTopicPartitionOffset(tp, watermarks, options);
                UpdateLimitUsingEnd(options);
                Consumer.Assign(tpo);
                Console.WriteLine($"Seeked in {watch.ElapsedMilliseconds} ms");
                watch.Restart();

                while (messages.Count < options.Limit)
                {
                    var result = Consumer.Consume(consumeTimeout);
                    if (result == null)
                    {
                        Console.WriteLine("Got null message. Must be timed out.");
                        break;
                    }
                    if (result.IsPartitionEOF)
                    {
                        Console.WriteLine("End of partition reached.");
                        break;
                    }

                    Console.WriteLine($"Got message in {watch.ElapsedMilliseconds} ms");
                    watch.Restart();

                    messages.Add(CreateMessage(result));

                    if (result.Offset == watermarks.High - 1)
                    {
                        Console.WriteLine("End of partition reached.");
                        break;
                    }
                }
                Console.WriteLine($"Got remaining messages in {watch.ElapsedMilliseconds} ms");
                watch.Stop();
            }

            return messages;
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

        public async Task<List<Message>> GetMessagesAsync(string topic, FetchOptions options)
        {
            return await Task.Run(() => GetMessages(topic, options));
        }

        public List<Message> GetMessages(string topicName, FetchOptions options)
        {
            Console.WriteLine($"Getting messages for topic {topicName}");
            var watch = new Stopwatch();
            watch.Start();

            ValidateTopic(topicName);
            var messages = new List<Message>();
            var topic = Topics[topicName];

            var requiredCount = options.Limit;
            var remaining = requiredCount;

            var tps = topic.Partitions.Select(partition => new Confluent.Kafka.TopicPartition(topicName, partition.Id)).ToList();
            var watermarks = QueryWatermarkOffsets(tps);
            Console.WriteLine("Got watermarks in {0} ms", watch.ElapsedMilliseconds);
            var tpos = new List<Confluent.Kafka.TopicPartitionOffset>();
            for (int i = 0; i < tps.Count; ++i)
            {
                options.Limit = remaining / (topic.PartitionCount - i);
                var tpo = CreateTopicPartitionOffset(tps[i], watermarks[i], options);
                tpos.Add(tpo);
                remaining -= options.Limit;
            }

            Consumer.Assign(tpos);
            Console.WriteLine("Assigned topic in {0} ms", watch.ElapsedMilliseconds);

            remaining = requiredCount;
            while (remaining > 0)
            {
                var result = Consumer.Consume(consumeTimeout);
                if (result == null)
                {
                    Console.WriteLine("Got null message. Must be timed out.");
                    break;
                }
                if (result.IsPartitionEOF)
                {
                    Console.WriteLine("End of partition reached.");
                    break;
                }

                messages.Add(CreateMessage(result));
                --remaining;
                Console.WriteLine("Got message in {0} ms", watch.ElapsedMilliseconds);
            }

            Console.WriteLine("Got {0} messages in {1} ms", messages.Count, watch.ElapsedMilliseconds);
            return messages;
        }

        private List<WatermarkOffsets> QueryWatermarkOffsets(List<Confluent.Kafka.TopicPartition> tps)
        {
            lock(Consumer)
            {
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
                int i = 0;
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
                offset = watermarks.High + 1 + offset;
            }
            if (offset < watermarks.Low)
            {
                offset = watermarks.Low;
            }
            else if (offset > watermarks.High)
            {
                offset = watermarks.High;
            }
            position.SetOffset(offset);
        }

        private void UpdateLimitUsingEnd(FetchOptions options)
        {
            if (options.End != null)
            {
                var end = options.End;

                // now end has a valid offset
                int distance = (int)(end.Offset - options.Start.Offset);
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

        private Message CreateMessage(ConsumeResult<byte[], byte[]> result)
        {
            long epochMillis = result.Message.Timestamp.UnixTimestampMs;
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
}