using System;
using System.Collections.Generic;
using Confluent.Kafka;
using FluentAssertions;
using KafkaLens.Core.Services;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Models;
using Xunit;

namespace KafkaLens.Core.Tests.Security;

public class SaslConfigurationTests
{
    private static ConsumerConfig Apply(ClusterSecurity security, string? password = "secret")
    {
        var secrets = new Dictionary<string, string>();
        if (password != null) secrets[ClusterSecretNames.SaslPassword] = password;
        var config = new ConsumerConfig();
        KafkaSecurityConfigurator.Apply(config, new KafkaConnectionSettings("b:9093", security, secrets));
        return config;
    }

    [Theory]
    [InlineData(KafkaSaslMechanism.Plain, SaslMechanism.Plain)]
    [InlineData(KafkaSaslMechanism.ScramSha256, SaslMechanism.ScramSha256)]
    [InlineData(KafkaSaslMechanism.ScramSha512, SaslMechanism.ScramSha512)]
    public void SaslSsl_SetsMechanismCredentialsAndVerifiedTls(KafkaSaslMechanism mechanism, SaslMechanism expected)
    {
        var config = Apply(new ClusterSecurity
        {
            Protocol = KafkaSecurityProtocol.SaslSsl, SaslMechanism = mechanism, SaslUsername = "alice"
        });

        config.SecurityProtocol.Should().Be(SecurityProtocol.SaslSsl);
        config.SaslMechanism.Should().Be(expected);
        config.SaslUsername.Should().Be("alice");
        config.SaslPassword.Should().Be("secret");
        config.EnableSslCertificateVerification.Should().BeTrue();
        config.SslEndpointIdentificationAlgorithm.Should().Be(SslEndpointIdentificationAlgorithm.Https);
    }

    [Fact]
    public void SaslPlaintext_SetsNoTls()
    {
        var config = Apply(new ClusterSecurity { Protocol = KafkaSecurityProtocol.SaslPlaintext, SaslUsername = "alice" });
        config.SecurityProtocol.Should().Be(SecurityProtocol.SaslPlaintext);
        config.SaslMechanism.Should().Be(SaslMechanism.Plain);
        config.SslCaLocation.Should().BeNull();
    }

    [Fact]
    public void MissingPassword_GivesActionableError()
    {
        var act = () => Apply(new ClusterSecurity { Protocol = KafkaSecurityProtocol.SaslSsl, SaslUsername = "alice" }, password: null);
        act.Should().Throw<ArgumentException>().WithMessage("*SASL password is missing*");
    }

    [Fact]
    public void MissingUsername_IsRejected()
    {
        var act = () => Apply(new ClusterSecurity { Protocol = KafkaSecurityProtocol.SaslSsl });
        act.Should().Throw<ArgumentException>().WithMessage("*user name*");
    }

    [Fact]
    public void Model_FlagsUnencryptedCredentials_AndSummarizes()
    {
        var plain = new ClusterSecurity { Protocol = KafkaSecurityProtocol.SaslPlaintext, SaslMechanism = KafkaSaslMechanism.ScramSha512, SaslUsername = "u" };
        plain.SendsCredentialsUnencrypted.Should().BeTrue();
        plain.Summary().Should().Be("SASL/SCRAM-SHA-512 (unencrypted)");

        var ssl = new ClusterSecurity { Protocol = KafkaSecurityProtocol.SaslSsl, SaslUsername = "u" };
        ssl.SendsCredentialsUnencrypted.Should().BeFalse();
        ssl.Summary().Should().Be("SASL/PLAIN + TLS");
        ssl.ApplicableSecretNames().Should().Contain(ClusterSecretNames.SaslPassword);
    }

    [Fact]
    public void Normalized_DropsSaslFieldsWhenNotUsingSasl_AndSaslPasswordIsDiscarded()
    {
        var tls = new ClusterSecurity { Protocol = KafkaSecurityProtocol.Ssl, SaslUsername = "u", SaslMechanism = KafkaSaslMechanism.ScramSha256 };
        var n = tls.Normalized();
        n.SaslUsername.Should().BeNull();
        n.SaslMechanism.Should().BeNull();

        ClusterSecurity.MergeSecrets(new Dictionary<string, string> { [ClusterSecretNames.SaslPassword] = "p" }, null, n)
            .Should().BeEmpty();
    }

    [Fact]
    public void SameAs_DetectsMechanismAndUserChanges()
    {
        var a = new ClusterSecurity { Protocol = KafkaSecurityProtocol.SaslSsl, SaslUsername = "u" };
        a.SameAs(new ClusterSecurity { Protocol = KafkaSecurityProtocol.SaslSsl, SaslUsername = "u", SaslMechanism = KafkaSaslMechanism.Plain }).Should().BeTrue();
        a.SameAs(new ClusterSecurity { Protocol = KafkaSecurityProtocol.SaslSsl, SaslUsername = "v" }).Should().BeFalse();
        a.SameAs(new ClusterSecurity { Protocol = KafkaSecurityProtocol.SaslSsl, SaslUsername = "u", SaslMechanism = KafkaSaslMechanism.ScramSha256 }).Should().BeFalse();
    }
}
