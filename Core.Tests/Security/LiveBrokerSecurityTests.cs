using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KafkaLens.Core.Services;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Models;
using Xunit;

namespace KafkaLens.Core.Tests.Security;

/// <summary>Runs only when a test broker from tools/test-brokers is available.</summary>
public sealed class LiveBrokerFactAttribute : FactAttribute
{
    public LiveBrokerFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(LiveBrokerSecurityTests.EnvVar)))
            Skip = $"Set {LiveBrokerSecurityTests.EnvVar} to the work dir of tools/test-brokers/secure-broker.sh";
    }
}

public sealed class LiveBrokerTheoryAttribute : TheoryAttribute
{
    public LiveBrokerTheoryAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(LiveBrokerSecurityTests.EnvVar)))
            Skip = $"Set {LiveBrokerSecurityTests.EnvVar} to the work dir of tools/test-brokers/secure-broker.sh";
    }
}

/// <summary>
/// End-to-end checks against a real broker with SASL/TLS listeners (see tools/test-brokers/README.md).
/// Skipped in normal test runs.
/// </summary>
public class LiveBrokerSecurityTests
{
    public const string EnvVar = "KAFKALENS_TEST_SECURE_BROKER";
    private static string WorkDir => Environment.GetEnvironmentVariable(EnvVar)!;
    private static string Ca => Path.Combine(WorkDir, "ca.pem");

    private static readonly KafkaConfig Config = new() { AdminMetadataTimeoutMs = 8000, QueryTopicsTimeoutMs = 8000, QueryWatermarkTimeoutMs = 8000 };

    private static async Task<(ConnectionValidationResult Result, IKafkaConsumer Consumer)> ValidateAsync(string address, ClusterSecurity security, string? password)
    {
        var secrets = new Dictionary<string, string>();
        if (password != null) secrets[ClusterSecretNames.SaslPassword] = password;
        var consumer = new ConsumerFactory(Config).CreateFor(new KafkaConnectionSettings(address, security, secrets));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return (await consumer.ValidateConnectionWithDetailsAsync(cts.Token), consumer);
    }

    [LiveBrokerTheory]
    [InlineData(KafkaSecurityProtocol.SaslPlaintext, "localhost:29092", KafkaSaslMechanism.Plain)]
    [InlineData(KafkaSecurityProtocol.SaslPlaintext, "localhost:29092", KafkaSaslMechanism.ScramSha256)]
    [InlineData(KafkaSecurityProtocol.SaslPlaintext, "localhost:29092", KafkaSaslMechanism.ScramSha512)]
    [InlineData(KafkaSecurityProtocol.SaslSsl, "localhost:29095", KafkaSaslMechanism.Plain)]
    [InlineData(KafkaSecurityProtocol.SaslSsl, "localhost:29095", KafkaSaslMechanism.ScramSha256)]
    [InlineData(KafkaSecurityProtocol.SaslSsl, "localhost:29095", KafkaSaslMechanism.ScramSha512)]
    public async Task CorrectCredentials_ConnectAndListTopics(KafkaSecurityProtocol protocol, string address, KafkaSaslMechanism mechanism)
    {
        var security = new ClusterSecurity { Protocol = protocol, SaslMechanism = mechanism, SaslUsername = "alice", SslCaLocation = Ca };
        var (result, consumer) = await ValidateAsync(address, security, "alice-secret");
        using (consumer)
        {
            result.Succeeded.Should().BeTrue(result.ErrorMessage);
            consumer.GetTopics().Should().Contain(t => t.Name == "secured.demo");
        }
    }

    [LiveBrokerTheory]
    [InlineData(KafkaSecurityProtocol.SaslPlaintext, "localhost:29092", KafkaSaslMechanism.Plain)]
    [InlineData(KafkaSecurityProtocol.SaslPlaintext, "localhost:29092", KafkaSaslMechanism.ScramSha512)]
    [InlineData(KafkaSecurityProtocol.SaslSsl, "localhost:29095", KafkaSaslMechanism.ScramSha256)]
    public async Task WrongPassword_FailsWithAuthenticationError(KafkaSecurityProtocol protocol, string address, KafkaSaslMechanism mechanism)
    {
        var security = new ClusterSecurity { Protocol = protocol, SaslMechanism = mechanism, SaslUsername = "alice", SslCaLocation = Ca };
        var (result, consumer) = await ValidateAsync(address, security, "wrong-password");
        using (consumer)
        {
            result.Succeeded.Should().BeFalse();
            result.ErrorMessage.Should().StartWith(KafkaErrorTranslator.AuthenticationMessage);
            (result.ErrorMessage + result.ErrorDetails).Should().NotContain("wrong-password");
        }
    }

    [LiveBrokerFact]
    public async Task Tls_WithTestCa_Succeeds()
    {
        var (result, consumer) = await ValidateAsync("localhost:29096", new ClusterSecurity { Protocol = KafkaSecurityProtocol.Ssl, SslCaLocation = Ca }, null);
        using (consumer) result.Succeeded.Should().BeTrue(result.ErrorMessage);
    }

    [LiveBrokerFact]
    public async Task Tls_WithoutTrustedCa_IsRejected()
    {
        var (result, consumer) = await ValidateAsync("localhost:29096", new ClusterSecurity { Protocol = KafkaSecurityProtocol.Ssl }, null);
        using (consumer)
        {
            result.Succeeded.Should().BeFalse("the test CA is not in the system trust store");
            result.ErrorMessage.Should().StartWith(KafkaErrorTranslator.TlsMessage);
        }
    }

    [LiveBrokerFact]
    public async Task Tls_HostnameMismatch_IsRejected()
    {
        // Certificate is for "localhost"; connecting by IP must fail hostname verification.
        var (result, consumer) = await ValidateAsync("127.0.0.1:29096", new ClusterSecurity { Protocol = KafkaSecurityProtocol.Ssl, SslCaLocation = Ca }, null);
        using (consumer) result.Succeeded.Should().BeFalse();
    }
}
