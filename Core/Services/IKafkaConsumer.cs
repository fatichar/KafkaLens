using KafkaLens.Shared.Models;

namespace KafkaLens.Core.Services
{
    public interface IKafkaConsumer
    {
        List<Topic> GetTopics();
        //List<Message> GetMessages(string topic, FetchOptions options);
        List<Message> GetMessages(string topic, int partition, FetchOptions options);
        List<Message> GetMessages(string topic, FetchOptions options);
        Task<List<Message>> GetMessagesAsync(string topic, int partition, FetchOptions options);
        Task<List<Message>> GetMessagesAsync(string topic, FetchOptions options);
    }
}