using KafkaLens.Shared.Models;

namespace KafkaLens.Core.Services;

public interface IStreamingKafkaConsumer
{
    IAsyncEnumerable<Message> StreamMessagesAsync(
        string topic,
        FetchOptions options,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<Message> StreamMessagesAsync(
        string topic,
        int partition,
        FetchOptions options,
        CancellationToken cancellationToken = default);
}
