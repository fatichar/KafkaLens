using Confluent.Kafka;
using KafkaLens.Shared.Models;

namespace KafkaLens.Core.Utils;

public static class MessageConverter
{
    public static Message CreateMessage(ConsumeResult<byte[], byte[]> result)
    {
        var epochMillis = result.Message.Timestamp.UnixTimestampMs;
        var headers = result.Message.Headers.ToDictionary(header =>
            header.Key, header => header.GetValueBytes());

        return new Message(epochMillis, headers, result.Message.Key, result.Message.Value)
        {
            Partition = result.Partition.Value,
            Offset = result.Offset.Value
        };
    }
}
