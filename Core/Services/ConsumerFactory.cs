using KafkaLens.Shared.Models;

namespace KafkaLens.Core.Services;

public class ConsumerFactory(KafkaConfig kafkaConfig)
{
    public virtual IKafkaConsumer CreateNew(string url)
    {
        return new ConfluentConsumer(url, kafkaConfig);
    }
}