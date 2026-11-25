using Confluent.SchemaRegistry;
using FluentAssertions;
using KafkaLens.Formatting;
using Xunit;

namespace KafkaLens.Formatting.Tests;

public class SchemaRegistryOptionsTests
{
    [Fact]
    public void ToConfig_WithoutAuth_OnlySetsUrlAndVerification()
    {
        var config = new SchemaRegistryOptions(" http://localhost:8081 ").ToConfig();
        config.Url.Should().Be("http://localhost:8081");
        config.BasicAuthUserInfo.Should().BeNull();
        config.EnableSslCertificateVerification.Should().BeTrue();
    }

    [Fact]
    public void ToConfig_WithBasicAuthAndCa()
    {
        var config = new SchemaRegistryOptions("https://psrc.example", "KEY", "SECRET", "/ca.pem").ToConfig();
        config.BasicAuthCredentialsSource.Should().Be(AuthCredentialsSource.UserInfo);
        config.BasicAuthUserInfo.Should().Be("KEY:SECRET");
        config.SslCaLocation.Should().Be("/ca.pem");
        config.EnableSslCertificateVerification.Should().BeTrue();
    }

    [Fact]
    public void CacheKey_DistinguishesCredentials_WithoutRevealingThem()
    {
        var a = new SchemaRegistryOptions("https://r", "u", "secret-1");
        var b = new SchemaRegistryOptions("https://r", "u", "secret-2");
        a.CacheKey().Should().NotBe(b.CacheKey()).And.NotContain("secret-1");
        a.ToString().Should().Be("https://r");
    }
}
