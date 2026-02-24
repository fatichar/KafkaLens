using Confluent.Kafka;
using KafkaLens.Shared.Models;

namespace KafkaLens.Core.Utils;

public static class MessageConverter
{
    public static Message CreateMessage(ConsumeResult<byte[], byte[]> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(result.Message);

        var epochMillis = result.Message.Timestamp.UnixTimestampMs;

        var headers = result.Message.Headers?.ToDictionary(header =>
            header.Key, header => header.GetValueBytes()) ?? new Dictionary<string, byte[]>();

        return new Message(epochMillis, headers, result.Message.Key, result.Message.Value)
        {
            Partition = result.Partition.Value,
            Offset = result.Offset.Value
        };
    }
}