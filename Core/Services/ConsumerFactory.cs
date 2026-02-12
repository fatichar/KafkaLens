namespace KafkaLens.Core.Services;

public class ConsumerFactory
{
    public virtual IKafkaConsumer CreateNew(string url)
    {
        return new ConfluentConsumer(url);
    }
}