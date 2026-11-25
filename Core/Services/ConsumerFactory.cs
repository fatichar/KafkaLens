using KafkaLens.Shared.Models;

namespace KafkaLens.Core.Services;

public class ConsumerFactory(KafkaConfig kafkaConfig)
{
    /// <summary>Creates a consumer for the given connection, applying TLS/authentication settings.</summary>
    public IKafkaConsumer CreateFor(KafkaConnectionSettings settings)
        => settings.IsPlain && settings.Secrets.Count == 0 ? CreateNew(settings.BootstrapServers) : CreateSecured(settings);

    public virtual IKafkaConsumer CreateNew(string url)
    {
        return new ConfluentConsumer(url, kafkaConfig);
    }

    protected virtual IKafkaConsumer CreateSecured(KafkaConnectionSettings settings)
    {
        return new ConfluentConsumer(settings, kafkaConfig);
    }
}
