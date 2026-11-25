using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using FluentAssertions;
using KafkaLens.Core.Services;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Models;
using Xunit;
using KafkaConfig = KafkaLens.Shared.Models.KafkaConfig;

namespace KafkaLens.Core.Tests.Security;

public class ConfluentCloudConfigurationTests
{
    private static readonly ClusterSecurity CloudSecurity = new()
    {
        Protocol = KafkaSecurityProtocol.SaslSsl, SaslMechanism = KafkaSaslMechanism.Plain, SaslUsername = "API_KEY"
    };

    [Fact]
    public void CloudSettings_MapToSaslSslPlain_WithVerifiedPublicCa()
    {
        var config = new ConsumerConfig();
        KafkaSecurityConfigurator.Apply(config, new KafkaConnectionSettings("pkc-abc12.eu-west-1.aws.confluent.cloud:9092", CloudSecurity,
            new Dictionary<string, string> { [ClusterSecretNames.SaslPassword] = "API_SECRET" }));

        config.SecurityProtocol.Should().Be(SecurityProtocol.SaslSsl);
        config.SaslMechanism.Should().Be(SaslMechanism.Plain);
        config.SaslUsername.Should().Be("API_KEY");
        config.SaslPassword.Should().Be("API_SECRET");
        config.EnableSslCertificateVerification.Should().BeTrue();
        config.SslEndpointIdentificationAlgorithm.Should().Be(SslEndpointIdentificationAlgorithm.Https);
    }

    [Theory]
    [InlineData("pkc-abc12.eu-west-1.aws.confluent.cloud:9092", KafkaSecurityProtocol.Plaintext, KafkaSaslMechanism.Plain, true)]
    [InlineData("pkc-abc12.eu-west-1.aws.confluent.cloud:9092", KafkaSecurityProtocol.SaslSsl, KafkaSaslMechanism.ScramSha512, true)]
    [InlineData("pkc-abc12.eu-west-1.aws.confluent.cloud:9092", KafkaSecurityProtocol.SaslSsl, KafkaSaslMechanism.Plain, false)]
    [InlineData("broker.internal:9092", KafkaSecurityProtocol.Plaintext, KafkaSaslMechanism.Plain, false)]
    public void Hint_SuggestsCloudSettings(string address, KafkaSecurityProtocol protocol, KafkaSaslMechanism mechanism, bool expectHint)
    {
        var hint = ClusterSecurity.HintFor(address, new ClusterSecurity { Protocol = protocol, SaslMechanism = mechanism });
        (hint != null).Should().Be(expectHint);
    }
}

/// <summary>Runs only when Confluent Cloud test credentials are provided (see docs/confluent-cloud.md).</summary>
public sealed class ConfluentCloudFactAttribute : FactAttribute
{
    public ConfluentCloudFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KAFKALENS_TEST_CCLOUD_BOOTSTRAP")))
            Skip = "Set KAFKALENS_TEST_CCLOUD_BOOTSTRAP, KAFKALENS_TEST_CCLOUD_KEY and KAFKALENS_TEST_CCLOUD_SECRET to run.";
    }
}

public class ConfluentCloudLiveTests
{
    private static string Env(string name) => Environment.GetEnvironmentVariable(name)!;

    private static async Task<(ConnectionValidationResult, IKafkaConsumer)> Validate(string secret)
    {
        var settings = new KafkaConnectionSettings(Env("KAFKALENS_TEST_CCLOUD_BOOTSTRAP"),
            new ClusterSecurity { Protocol = KafkaSecurityProtocol.SaslSsl, SaslMechanism = KafkaSaslMechanism.Plain, SaslUsername = Env("KAFKALENS_TEST_CCLOUD_KEY") },
            new Dictionary<string, string> { [ClusterSecretNames.SaslPassword] = secret });
        var consumer = new ConsumerFactory(new KafkaConfig { AdminMetadataTimeoutMs = 15000, QueryTopicsTimeoutMs = 15000 }).CreateFor(settings);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        return (await consumer.ValidateConnectionWithDetailsAsync(cts.Token), consumer);
    }

    [ConfluentCloudFact]
    public async Task ValidKey_ConnectsAndListsTopics()
    {
        var (result, consumer) = await Validate(Env("KAFKALENS_TEST_CCLOUD_SECRET"));
        using (consumer)
        {
            result.Succeeded.Should().BeTrue(result.ErrorMessage);
            consumer.GetTopics().Should().NotBeNull();
        }
    }

    [ConfluentCloudFact]
    public async Task InvalidSecret_ReportsAuthenticationFailure()
    {
        var (result, consumer) = await Validate("definitely-not-the-secret");
        using (consumer)
        {
            result.Succeeded.Should().BeFalse();
            result.ErrorMessage.Should().StartWith(KafkaErrorTranslator.AuthenticationMessage);
        }
    }
}
