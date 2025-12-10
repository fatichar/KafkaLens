namespace KafkaLens.Formatting;

public interface IMessageFormatter
{
    public string Name { get; }

    public string? Format(byte[] data, bool prettyPrint);
    // public string? Format(string objBody);
    string? Format(byte[] data, string searchText, bool useObjectFilter = true);
}