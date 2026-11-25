using NSubstitute;
using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Security;
using Xunit;

namespace KafkaLens.Core.Tests.Security;

public sealed class ClusterInfoRepositorySecretsTests : IDisposable
{
    private readonly string file = Path.Combine(Path.GetTempPath(), $"kl-secrets-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(file)) File.Delete(file);
    }

    /// <summary>Stand-in for an OS keyring.</summary>
    private sealed class FakeKeyring : ISecretStore
    {
        public readonly Dictionary<string, string> Items = new();
        public string BackendName => "fake keyring";
        public bool IsPersistent => true;
        public string? Get(string key) => Items.GetValueOrDefault(key);
        public void Set(string key, string secret) => Items[key] = secret;
        public void Delete(string key) => Items.Remove(key);
    }

    private static ClusterInfo NewCluster(string password) => new("c1", "Prod", "broker:9093")
    {
        Secrets = new() { [ClusterSecretNames.SaslPassword] = password }
    };

    [Fact]
    public void Add_StoresSecretInKeyring_NotInFile()
    {
        var keyring = new FakeKeyring();
        var repo = new ClusterInfoRepository(file, keyring);

        repo.Add(NewCluster("hunter2"));

        File.ReadAllText(file).Should().NotContain("hunter2").And.Contain("sasl.password");
        keyring.Items.Should().ContainKey("cluster/c1/sasl.password").WhoseValue.Should().Be("hunter2");
    }

    [Fact]
    public void Reload_ResolvesSecretsFromKeyring()
    {
        var keyring = new FakeKeyring();
        new ClusterInfoRepository(file, keyring).Add(NewCluster("hunter2"));

        var reloaded = new ClusterInfoRepository(file, keyring).GetById("c1");

        reloaded.GetSecret(ClusterSecretNames.SaslPassword).Should().Be("hunter2");
    }

    [Fact]
    public void Update_ReplacesAndRemovesSecrets()
    {
        var keyring = new FakeKeyring();
        var repo = new ClusterInfoRepository(file, keyring);
        repo.Add(NewCluster("old"));

        repo.Update(NewCluster("new"));
        keyring.Items["cluster/c1/sasl.password"].Should().Be("new");

        repo.Update(new ClusterInfo("c1", "Prod", "broker:9093") { Secrets = new() });
        keyring.Items.Should().BeEmpty();
        File.ReadAllText(file).Should().NotContain("storedSecrets");
    }

    [Fact]
    public void Update_WithoutSecrets_KeepsStoredSecrets()
    {
        var keyring = new FakeKeyring();
        var repo = new ClusterInfoRepository(file, keyring);
        repo.Add(NewCluster("keep-me"));

        repo.Update(new ClusterInfo("c1", "Renamed", "broker:9093")); // Secrets == null: untouched

        repo.GetById("c1").GetSecret(ClusterSecretNames.SaslPassword).Should().Be("keep-me");
        keyring.Items.Should().ContainKey("cluster/c1/sasl.password");
        new ClusterInfoRepository(file, keyring).GetById("c1").Name.Should().Be("Renamed");
    }

    [Fact]
    public void Delete_RemovesSecretsFromKeyring()
    {
        var keyring = new FakeKeyring();
        var repo = new ClusterInfoRepository(file, keyring);
        repo.Add(NewCluster("bye"));
        repo.Add(new ClusterInfo("c2", "Other", "b:1") { Secrets = new() { [ClusterSecretNames.SslKeyPassword] = "x" } });

        repo.Delete("c1");
        keyring.Items.Keys.Should().Equal("cluster/c2/ssl.key.password");

        repo.DeleteAll();
        keyring.Items.Should().BeEmpty();
    }

    private void WritePlaintextConfig() => File.WriteAllText(file, """
        { "clusters": [ { "id": "c1", "name": "Prod", "address": "broker:9093", "protocol": null,
                          "secrets": { "sasl.password": "plain-old" } } ] }
        """);

    [Fact]
    public void Load_MigratesPlaintextSecretsToKeyring_AndRewritesFile()
    {
        WritePlaintextConfig();
        var keyring = new FakeKeyring();

        var repo = new ClusterInfoRepository(file, keyring);

        repo.GetById("c1").GetSecret(ClusterSecretNames.SaslPassword).Should().Be("plain-old");
        keyring.Items["cluster/c1/sasl.password"].Should().Be("plain-old");
        var json = File.ReadAllText(file);
        json.Should().NotContain("plain-old").And.Contain("storedSecrets");

        new ClusterInfoRepository(file, keyring).GetById("c1").GetSecret(ClusterSecretNames.SaslPassword).Should().Be("plain-old");
    }

    [Fact]
    public void Load_WithoutPersistentStore_KeepsPlaintextFileUntouched_ButUsesSecret()
    {
        WritePlaintextConfig();
        var before = File.ReadAllText(file);

        var repo = new ClusterInfoRepository(file, new InMemorySecretStore());

        repo.GetById("c1").GetSecret(ClusterSecretNames.SaslPassword).Should().Be("plain-old");
        File.ReadAllText(file).Should().Be(before);
    }

    [Fact]
    public void WithoutPersistentStore_NewSecretsAreNeverWrittenToDisk()
    {
        var repo = new ClusterInfoRepository(file, new InMemorySecretStore());

        repo.Add(NewCluster("session-only"));

        File.ReadAllText(file).Should().NotContain("session-only").And.NotContain("storedSecrets");
        repo.GetById("c1").GetSecret(ClusterSecretNames.SaslPassword).Should().Be("session-only");
    }

    [Fact]
    public void WithoutPersistentStore_ReplacingAPlaintextSecret_DropsTheStaleValue()
    {
        WritePlaintextConfig();
        var repo = new ClusterInfoRepository(file, new InMemorySecretStore());

        repo.Update(NewCluster("changed"));

        var json = File.ReadAllText(file);
        json.Should().NotContain("plain-old").And.NotContain("changed");
    }

    [Fact]
    public void FailingKeyring_DuringMigration_LeavesPlaintextInPlace()
    {
        WritePlaintextConfig();
        var keyring = NSubstitute.Substitute.For<ISecretStore>();
        keyring.IsPersistent.Returns(true);
        keyring.When(k => k.Set(NSubstitute.Arg.Any<string>(), NSubstitute.Arg.Any<string>()))
            .Do(_ => throw new InvalidOperationException("keyring locked"));

        var repo = new ClusterInfoRepository(file, keyring);

        repo.GetById("c1").GetSecret(ClusterSecretNames.SaslPassword).Should().Be("plain-old");
        File.ReadAllText(file).Should().Contain("plain-old");
    }

    [Fact]
    public void UnreadableSecrets_SurviveUnrelatedUpdates_AndResolveLater()
    {
        var keyring = new FakeKeyring();
        new ClusterInfoRepository(file, keyring).Add(NewCluster("kept"));

        // Next start: no keyring -> session-only fallback, the secret cannot be read.
        var offline = new ClusterInfoRepository(file, new InMemorySecretStore());
        var loaded = offline.GetById("c1");
        loaded.GetSecret(ClusterSecretNames.SaslPassword).Should().BeNull();
        loaded.Name = "Renamed";
        offline.Update(loaded); // same instance, Secrets is an empty (loaded) dictionary

        File.ReadAllText(file).Should().Contain("sasl.password");
        new ClusterInfoRepository(file, keyring).GetById("c1").GetSecret(ClusterSecretNames.SaslPassword).Should().Be("kept");
    }

    [Fact]
    public void UnreadableSecret_IsNotDeletedFromKeyring_OnUnrelatedUpdate()
    {
        var keyring = new FakeKeyring();
        new ClusterInfoRepository(file, keyring).Add(NewCluster("kept"));
        var flaky = Substitute.For<ISecretStore>();
        flaky.IsPersistent.Returns(true);
        flaky.Get(Arg.Any<string>()).Returns(_ => throw new InvalidOperationException("keyring locked"));

        var repo = new ClusterInfoRepository(file, flaky);
        var loaded = repo.GetById("c1");
        loaded.Address = "other:9093";
        repo.Update(loaded);

        flaky.DidNotReceive().Delete(Arg.Any<string>());
        File.ReadAllText(file).Should().Contain("sasl.password");
    }
}
