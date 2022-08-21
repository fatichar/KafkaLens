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

        public async Task<List<Message>> GetMessagesAsync(string topic, FetchOptions options)
        {
            return await Task.Run(() => GetMessages(topic, options));
        }

        public List<Message> GetMessages(string topicName, int partition, FetchOptions options)
        {
            var watch = new Stopwatch();
            watch.Start();
            var tp = ValidateTopicPartition(topicName, partition);
            var messages = new List<Message>();
            lock(Consumer)
            {
                Console.WriteLine($"Got lock in {watch.ElapsedMilliseconds} ms");
                watch.Restart();
                Consumer.Assign(tp);
                Console.WriteLine($"assigned tp in {watch.ElapsedMilliseconds} ms");
                watch.Restart();
                var watermarks = Consumer.QueryWatermarkOffsets(tp, queryWatermarkTimeout);
                Console.WriteLine($"Got watermarks in {watch.ElapsedMilliseconds} ms");
                watch.Restart();
                var tpo = CreateTopicPartitionOffset(tp, watermarks, options);
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

        public List<Message> GetMessages(string topicName, FetchOptions options)
        {
            ValidateTopic(topicName);
            var topicMessages = new List<Message>();
            var topic = Topics[topicName];

            var remaining = options.Limit;

            for (int i = 0; i < topic.PartitionCount; i++)
            {
                options.Limit = remaining / (topic.PartitionCount - i);

                var messages = GetMessages(topicName, i, options);
                topicMessages.AddRange(messages);

                remaining -= messages.Count;
            }
            return topicMessages;
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
            switch (options.From)
            {
                case FetchOptions.FetchPosition.START:
                    return new(tp, watermarks.Low);
                case FetchOptions.FetchPosition.TIMESTAMP:
                    break;
                case FetchOptions.FetchPosition.OFFSET:
                    return new(tp, options.Offset);
                case FetchOptions.FetchPosition.END:
                    return new(tp, Math.Max(watermarks.High - options.Limit, watermarks.Low));
            }

            return new(tp, watermarks.High - options.Limit);
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