using System.Collections.ObjectModel;
using KafkaLens.Shared.Models;

namespace KafkaLens.Core.Services
{
    public interface IKafkaConsumer
    {
        List<Topic> GetTopics();
        MessageStream GetMessagesAsync(string topic, int partition, FetchOptions options);
        MessageStream GetMessagesAsync(string topic, FetchOptions options);
    }
}