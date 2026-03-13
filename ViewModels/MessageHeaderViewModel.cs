namespace KafkaLens.ViewModels;

public sealed class MessageHeaderViewModel
{
    public string Name { get; }
    public string Value { get; }

    public MessageHeaderViewModel(string name, string value)
    {
        Name = name;
        Value = value;
    }
}
