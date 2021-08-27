using Confluent.Kafka;
using KafkaLens.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace KafkaLens.Server.Services
{
    class ConfluentConsumer : IKafkaConsumer, IDisposable
    {
        private readonly double queryWatermarkTimeout = 2;
        private readonly double queryTopicsTimeout = 5;

        public string ServersUrl { get; }

        private IAdminClient adminClient;
        private IConsumer<string, string> consumer;

        public ConfluentConsumer(string url)
        {
            ServersUrl = url;

            adminClient = CreateAdminClient();

            try
            {
                ConsumerConfig config = CreateConsumerConfig();
                try
                {
                    consumer = new ConsumerBuilder<string, string>(config).Build();
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

        public IList<Topic> GetTopics()
        {
            var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(queryTopicsTimeout));

            var topics = metadata.Topics
                            .ConvertAll(topic => new Topic(topic.Topic, topic.Partitions.Count));
            topics.Sort(CompareTopics);

            return topics;
        }

        private int CompareTopics(Topic x, Topic y)
        {
            return x.Name.CompareTo(y.Name);
        }

        public void Dispose()
        {
            consumer.Dispose();
        }
    }
}
