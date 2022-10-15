namespace KafkaLens.Formatting;

public interface IMessageFormatter
{
    public string Name { get; }

    public string? Format(byte[] data);
}