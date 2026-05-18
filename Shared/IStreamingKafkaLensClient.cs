using System.Collections.Generic;
using System.Threading;
using KafkaLens.Shared.Models;

namespace KafkaLens.Shared;

public interface IStreamingKafkaLensClient
{
    IAsyncEnumerable<Message> StreamMessagesAsync(
        string clusterId,
        string topic,
        FetchOptions options,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<Message> StreamMessagesAsync(
        string clusterId,
        string topic,
        int partition,
        FetchOptions options,
        CancellationToken cancellationToken = default);
}
