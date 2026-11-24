using Confluent.Kafka;
using ClientConfig = Confluent.Kafka.ClientConfig;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Models;

namespace KafkaLens.Core.Services;

/// <summary>Maps <see cref="KafkaConnectionSettings"/> onto librdkafka client configuration.</summary>
public static class KafkaSecurityConfigurator
{
    public static void Apply(ClientConfig config, KafkaConnectionSettings settings)
    {
        var security = settings.Security.Normalized();
        var problem = security.Validate();
        if (problem != null) throw new ArgumentException(problem);

        config.BootstrapServers = settings.BootstrapServers;
        config.SecurityProtocol = security.Protocol switch
        {
            KafkaSecurityProtocol.Ssl => SecurityProtocol.Ssl,
            KafkaSecurityProtocol.SaslSsl => SecurityProtocol.SaslSsl,
            KafkaSecurityProtocol.SaslPlaintext => SecurityProtocol.SaslPlaintext,
            _ => SecurityProtocol.Plaintext
        };

        if (security.UsesSasl)
        {
            config.SaslMechanism = security.EffectiveSaslMechanism switch
            {
                KafkaSaslMechanism.ScramSha256 => SaslMechanism.ScramSha256,
                KafkaSaslMechanism.ScramSha512 => SaslMechanism.ScramSha512,
                _ => SaslMechanism.Plain
            };
            config.SaslUsername = security.SaslUsername;
            config.SaslPassword = settings.GetSecret(ClusterSecretNames.SaslPassword)
                ?? throw new ArgumentException("The SASL password is missing. Enter it in the cluster settings.");
        }

        if (!security.UsesTls) return;

        // Certificate validation is never relaxed: the broker certificate must chain to a trusted CA
        // and its hostname must match the bootstrap address.
        config.EnableSslCertificateVerification = true;
        config.SslEndpointIdentificationAlgorithm = SslEndpointIdentificationAlgorithm.Https;

        if (security.SslCaLocation != null)
            config.SslCaLocation = security.SslCaLocation;
        else if (!OperatingSystem.IsWindows())
            // Windows uses the system certificate store by default; elsewhere let librdkafka find the OS CA bundle.
            config.SslCaLocation = "probe";

        if (security.SslCertificateLocation != null)
        {
            config.SslCertificateLocation = security.SslCertificateLocation;
            config.SslKeyLocation = security.SslKeyLocation;
            var keyPassword = settings.GetSecret(ClusterSecretNames.SslKeyPassword);
            if (keyPassword != null) config.SslKeyPassword = keyPassword;
        }
    }
}
