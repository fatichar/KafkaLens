using System;
using System.IO;
using FluentAssertions;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Entities;
using Xunit;

namespace KafkaLens.Core.Tests.DataAccess;

public class ClusterInfoRepositorySchemaRegistryTests : IDisposable
{
    private readonly string tempFilePath;

    public ClusterInfoRepositorySchemaRegistryTests()
    {
        tempFilePath = Path.Combine(Path.GetTempPath(), $"cluster_info_test_{Guid.NewGuid()}.json");
    }

    public void Dispose()
    {
        if (File.Exists(tempFilePath))
        {
            File.Delete(tempFilePath);
        }
    }

    [Fact]
    public void Add_And_GetAll_ShouldPersistSchemaRegistryUrl()
    {
        var repo = new ClusterInfoRepository(tempFilePath);
        var added = repo.Add("TestCluster", "localhost:9092", "http://schema-registry:8081");

        added.SchemaRegistryUrl.Should().Be("http://schema-registry:8081");

        // Reload from disk
        var repo2 = new ClusterInfoRepository(tempFilePath);
        var loaded = repo2.GetById(added.Id);

        loaded.Name.Should().Be("TestCluster");
        loaded.Address.Should().Be("localhost:9092");
        loaded.SchemaRegistryUrl.Should().Be("http://schema-registry:8081");
    }

    [Fact]
    public void Update_ShouldUpdateSchemaRegistryUrl()
    {
        var repo = new ClusterInfoRepository(tempFilePath);
        var added = repo.Add("TestCluster", "localhost:9092");
        added.SchemaRegistryUrl.Should().BeNull();

        added.SchemaRegistryUrl = "http://schema-registry-updated:8081";
        repo.Update(added);

        var repo2 = new ClusterInfoRepository(tempFilePath);
        var loaded = repo2.GetById(added.Id);

        loaded.SchemaRegistryUrl.Should().Be("http://schema-registry-updated:8081");
    }
}
