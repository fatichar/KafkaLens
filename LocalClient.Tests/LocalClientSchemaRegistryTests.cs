using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Models;
using KafkaLens.Shared.Security;
using Xunit;

namespace KafkaLens.Clients.Tests;

public sealed class LocalClientSchemaRegistryTests : System.IDisposable
{
    private readonly string file = Path.Combine(Path.GetTempPath(), $"kl-sr-{System.Guid.NewGuid():N}.json");
    public void Dispose() { if (File.Exists(file)) File.Delete(file); }

    [Fact]
    public async Task SchemaRegistry_ResolvesUrlCredentialsAndCa_PasswordOnlyInStore()
    {
        var store = new InMemorySecretStore();
        var sut = new LocalClient(new ClusterInfoRepository(file, store), new KafkaConfig());

        var cluster = await sut.AddAsync(new NewKafkaCluster("c", "b:9092", "https://registry.example")
        {
            Security = new ClusterSecurity { SchemaRegistryUsername = "sr-key", SchemaRegistryCaLocation = "/ca.pem" },
            Secrets = new() { [ClusterSecretNames.SchemaRegistryPassword] = "sr-secret" }
        });

        sut.GetSchemaRegistry(cluster.Id).Should().Be(new SchemaRegistrySettings("https://registry.example", "sr-key", "sr-secret", "/ca.pem"));
        File.ReadAllText(file).Should().Contain("schemaRegistryUsername").And.NotContain("sr-secret");
    }

    [Fact]
    public async Task SchemaRegistry_NotConfigured_ReturnsNull()
    {
        var sut = new LocalClient(new ClusterInfoRepository(file), new KafkaConfig());
        var cluster = await sut.AddAsync(new NewKafkaCluster("c", "b:9092"));
        sut.GetSchemaRegistry(cluster.Id).Should().BeNull();
    }

    [Fact]
    public void RegistryUsername_MakesRegistryPasswordApplicable_EvenWithPlaintextKafka()
    {
        var security = new ClusterSecurity { SchemaRegistryUsername = "u" };
        security.IsDefault.Should().BeFalse();
        ClusterSecurity.MergeSecrets(null, new Dictionary<string, string> { [ClusterSecretNames.SchemaRegistryPassword] = "p" }, security)
            .Should().ContainKey(ClusterSecretNames.SchemaRegistryPassword);
    }
}
