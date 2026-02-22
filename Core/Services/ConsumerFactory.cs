using KafkaLens.Shared.Models;

namespace KafkaLens.Core.Services;

public class ConsumerFactory
{
    private readonly KafkaConfig kafkaConfig;

    public ConsumerFactory(KafkaConfig kafkaConfig)
    {
        this.kafkaConfig = kafkaConfig;
    }

    public virtual IKafkaConsumer CreateNew(string url)
    {
        return new ConfluentConsumer(url, kafkaConfig);
    }
}
