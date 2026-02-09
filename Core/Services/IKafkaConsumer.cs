using KafkaLens.Shared.Models;

namespace KafkaLens.Core.Services;

/// <summary>
/// Consumer for a kafka cluster.
/// </summary>
public interface IKafkaConsumer
{
    List<Topic> GetTopics();
    MessageStream GetMessageStream(string topic, int partition, FetchOptions options);
    MessageStream GetMessageStream(string topic, FetchOptions options);
    Task<List<Message>> GetMessagesAsync(string topic, int partition, FetchOptions options);
    Task<List<Message>> GetMessagesAsync(string topic, FetchOptions options);
}