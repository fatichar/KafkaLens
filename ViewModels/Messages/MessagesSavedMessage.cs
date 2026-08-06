namespace KafkaLens.ViewModels.Messages;

public sealed class MessagesSavedMessage(string directory, int count)
{
    public string Directory { get; } = directory;
    public int Count { get; } = count;
}
