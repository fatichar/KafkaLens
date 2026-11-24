using System;
using System.Collections.Generic;
using Confluent.Kafka;
using FluentAssertions;
using KafkaLens.Core.Services;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Models;
using Xunit;

namespace KafkaLens.Core.Tests.Security;

public class KafkaSecurityConfiguratorTests
{
    private static ConsumerConfig Apply(ClusterSecurity security, Dictionary<string, string>? secrets = null)
    {
        var config = new ConsumerConfig();
        KafkaSecurityConfigurator.Apply(config, new KafkaConnectionSettings("broker:9093", security, secrets ?? new()));
        return config;
    }

    [Fact]
    public void Plaintext_SetsNoTlsOptions()
    {
        var config = Apply(new ClusterSecurity());
        config.SecurityProtocol.Should().Be(SecurityProtocol.Plaintext);
        config.SslCaLocation.Should().BeNull();
        config.BootstrapServers.Should().Be("broker:9093");
    }

    [Fact]
    public void Ssl_AlwaysVerifiesCertificateAndHostname()
    {
        var config = Apply(new ClusterSecurity { Protocol = KafkaSecurityProtocol.Ssl });

        config.SecurityProtocol.Should().Be(SecurityProtocol.Ssl);
        config.EnableSslCertificateVerification.Should().BeTrue();
        config.SslEndpointIdentificationAlgorithm.Should().Be(SslEndpointIdentificationAlgorithm.Https);
        if (OperatingSystem.IsWindows()) config.SslCaLocation.Should().BeNull(); // Windows certificate store
        else config.SslCaLocation.Should().Be("probe");                         // OS CA bundle
    }

    [Fact]
    public void Ssl_UsesCustomCa()
    {
        Apply(new ClusterSecurity { Protocol = KafkaSecurityProtocol.Ssl, SslCaLocation = " /etc/ca.pem " })
            .SslCaLocation.Should().Be("/etc/ca.pem");
    }

    [Fact]
    public void MutualTls_SetsCertificateKeyAndKeyPassword()
    {
        var config = Apply(new ClusterSecurity
        {
            Protocol = KafkaSecurityProtocol.Ssl,
            SslCertificateLocation = "/c/client.pem",
            SslKeyLocation = "/c/client.key"
        }, new() { [ClusterSecretNames.SslKeyPassword] = "keypass" });

        config.SslCertificateLocation.Should().Be("/c/client.pem");
        config.SslKeyLocation.Should().Be("/c/client.key");
        config.SslKeyPassword.Should().Be("keypass");
    }

    [Fact]
    public void CertificateWithoutKey_IsRejected()
    {
        var act = () => Apply(new ClusterSecurity { Protocol = KafkaSecurityProtocol.Ssl, SslCertificateLocation = "/c/client.pem" });
        act.Should().Throw<ArgumentException>().WithMessage("*certificate and its private key*");
    }

    [Fact]
    public void TlsFields_AreIgnoredForPlaintext()
    {
        var config = Apply(new ClusterSecurity { Protocol = KafkaSecurityProtocol.Plaintext, SslCaLocation = "/etc/ca.pem" });
        config.SslCaLocation.Should().BeNull();
    }
}

public class ClusterSecurityTests
{
    [Fact]
    public void MergeSecrets_NewValuesWin_AndInapplicableAreDropped()
    {
        var mtls = new ClusterSecurity { Protocol = KafkaSecurityProtocol.Ssl, SslCertificateLocation = "c", SslKeyLocation = "k" };
        ClusterSecurity.MergeSecrets(
                new Dictionary<string, string> { [ClusterSecretNames.SslKeyPassword] = "old", ["unrelated"] = "x" },
                new Dictionary<string, string> { [ClusterSecretNames.SslKeyPassword] = "new" }, mtls)
            .Should().Equal(new Dictionary<string, string> { [ClusterSecretNames.SslKeyPassword] = "new" });

        // Empty entries keep the stored value.
        ClusterSecurity.MergeSecrets(
                new Dictionary<string, string> { [ClusterSecretNames.SslKeyPassword] = "old" },
                new Dictionary<string, string> { [ClusterSecretNames.SslKeyPassword] = "" }, mtls)
            [ClusterSecretNames.SslKeyPassword].Should().Be("old");

        // Switching to plaintext discards the key password.
        ClusterSecurity.MergeSecrets(
                new Dictionary<string, string> { [ClusterSecretNames.SslKeyPassword] = "old" }, null, new ClusterSecurity())
            .Should().BeEmpty();
    }

    [Fact]
    public void Normalized_TrimsAndClearsUnusedFields()
    {
        var n = new ClusterSecurity { Protocol = KafkaSecurityProtocol.Plaintext, SslCaLocation = "ca" }.Normalized();
        n.SslCaLocation.Should().BeNull();
        new ClusterSecurity { Protocol = KafkaSecurityProtocol.Ssl, SslCaLocation = "  " }.Normalized().SslCaLocation.Should().BeNull();
    }

    [Fact]
    public void Fingerprint_ChangesWithSecurityAndSecrets_ButDoesNotContainSecrets()
    {
        var ssl = new ClusterSecurity { Protocol = KafkaSecurityProtocol.Ssl, SslCertificateLocation = "c", SslKeyLocation = "k" };
        var a = new KafkaConnectionSettings("b:1", ssl, new Dictionary<string, string> { ["ssl.key.password"] = "one" }).Fingerprint();
        var b = new KafkaConnectionSettings("b:1", ssl, new Dictionary<string, string> { ["ssl.key.password"] = "two" }).Fingerprint();
        var plain = KafkaConnectionSettings.Plain("b:1").Fingerprint();

        a.Should().NotBe(b).And.NotBe(plain).And.NotContain("one");
        new KafkaConnectionSettings("b:1", ssl, new Dictionary<string, string>()).ToString().Should().Be("b:1 (Ssl)");
    }

    [Fact]
    public void ClusterInfo_SerializesSecurity_WithoutSecrets()
    {
        var info = new ClusterInfo("id", "n", "b:1")
        {
            Security = new ClusterSecurity { Protocol = KafkaSecurityProtocol.Ssl, SslCaLocation = "/ca.pem" },
            Secrets = new() { [ClusterSecretNames.SslKeyPassword] = "never-serialized" }
        };
        var json = System.Text.Json.JsonSerializer.Serialize(info);
        json.Should().Contain("\"protocol\":\"Ssl\"").And.Contain("/ca.pem").And.NotContain("never-serialized");

        var back = System.Text.Json.JsonSerializer.Deserialize<ClusterInfo>(json)!;
        back.Security!.Protocol.Should().Be(KafkaSecurityProtocol.Ssl);
    }
}
