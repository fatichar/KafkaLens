namespace KafkaLens.Shared.Models;

public class TopicPartition
{
    public TopicPartition(string topic, int partition)
    {
        Topic = topic;
        Partition = partition;
    }

    public string Topic { get; set; }

    public int Partition{ get; }
}