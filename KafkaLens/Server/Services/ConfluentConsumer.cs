using Confluent.Kafka;
using KafkaLens.Shared.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KafkaLens.Server.Services
{
    class ConfluentConsumer : IKafkaConsumer, IDisposable
    {
        private readonly TimeSpan queryWatermarkTimeout = TimeSpan.FromSeconds(10);
        private readonly TimeSpan queryTopicsTimeout = TimeSpan.FromSeconds(10);
        private readonly TimeSpan consumeTimeout = TimeSpan.FromSeconds(10);

        public string ServersUrl { get; }

        private IConsumer<byte[], byte[]> consumer;
        private IAdminClient adminClient;

        public ConfluentConsumer(string url)
        {
            ServersUrl = url;

            try
            {
                ConsumerConfig config = CreateConsumerConfig();
                adminClient = CreateAdminClient();
                try
                {
                    consumer = new ConsumerBuilder<byte[], byte[]>(config).Build();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private ConsumerConfig CreateConsumerConfig()
        {
            return new ConsumerConfig
            {
                GroupId = "KafkaLens.Server",
                ClientId = "KafkaLens.Server",
                BootstrapServers = ServersUrl,
                EnableAutoOffsetStore = false,
                EnableAutoCommit = false
            };
        }

        private IAdminClient CreateAdminClient()
        {
            var config = new AdminClientConfig
            {
                BootstrapServers = ServersUrl
            };
            return new AdminClientBuilder(config).Build();
        }

        public async Task<List<Topic>> GetTopicsAsync()
        {
            return await Task.Run(GetTopics);
        }

        private List<Topic> GetTopics()
        {
            var metadata = adminClient.GetMetadata(queryTopicsTimeout);

            var topics = metadata.Topics
                .ConvertAll(topic => new Topic(topic.Topic, topic.Partitions.Count));

            return topics;
        }

        public List<Message> GetMessages(string topic, FetchOptions options)
        {
            throw new NotImplementedException();
        }

        public async Task<List<Message>> GetMessagesAsync(string topic, int partition, FetchOptions options)
        {
            return await Task.Run(() => GetMessages(topic, partition, options));
        }

        private List<Message> GetMessages(string topic, int partition, FetchOptions options)
        {
            var watch = new Stopwatch();
            watch.Start();
            TopicPartition tp = new(topic, partition);
            var messages = new List<Message>();
            lock (consumer)
            {
                Console.WriteLine($"Got lock in {watch.ElapsedMilliseconds} ms");
                watch.Restart();
                consumer.Assign(tp);
                Console.WriteLine($"assigned tp in {watch.ElapsedMilliseconds} ms");
                watch.Restart();
                var watermarks = consumer.QueryWatermarkOffsets(tp, queryWatermarkTimeout);
                Console.WriteLine($"Got watermarks in {watch.ElapsedMilliseconds} ms");
                watch.Restart();
                var tpo = CreateTopicPartitionOffset(tp, watermarks, options);
                consumer.Seek(tpo);
                Console.WriteLine($"Seeked in {watch.ElapsedMilliseconds} ms");
                watch.Restart();


                Console.WriteLine($"Got first message in {watch.ElapsedMilliseconds} ms");
                watch.Restart();
                while (messages.Count < options.Limit)
                {
                    var result = consumer.Consume(consumeTimeout);
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
                }
                Console.WriteLine($"Got remaining messages in {watch.ElapsedMilliseconds} ms");
                watch.Stop();
            }

            return messages;
        }

        private TopicPartitionOffset CreateTopicPartitionOffset(TopicPartition tp, WatermarkOffsets watermarks, FetchOptions options)
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
                    return new(tp, watermarks.High - options.Limit);
            }

            return new(tp, watermarks.High - options.Limit);
        }

        private Message CreateMessage(ConsumeResult<byte[], byte[]> result)
        {
            long epochMillis = result.Message.Timestamp.UnixTimestampMs;
            var headers = result.Message.Headers.ToDictionary(header => 
                    header.Key, header => header.GetValueBytes());

            return new Message(epochMillis, headers, result.Message.Key, result.Message.Value);
        }

        #region IDisposable implemenatation
        public void Dispose()
        {
            consumer.Dispose();
        }
        #endregion IDisposable implemenatation
    }
}
