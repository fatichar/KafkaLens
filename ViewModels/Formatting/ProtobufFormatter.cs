namespace KafkaLens.Formatting;

class ProtobufFormatter : IMessageFormatter
{
    public string Name { get; } = "Protobuf";
    public string? Format(byte[] data)
    {
        throw new NotImplementedException();
    }
}