namespace KafkaLens.Client.Formatters;

public interface IMessageFormatter
{
    string DisplayName { get; set; }

    string Format(string data);
}