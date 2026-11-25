using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using KafkaLens.Shared.Security;
using Xunit;

namespace KafkaLens.Core.Tests.Security;

public class SecretStoreTests
{
    [Fact]
    public void InMemoryStore_IsNotPersistent_AndRoundTrips()
    {
        var store = new InMemorySecretStore();
        store.IsPersistent.Should().BeFalse();
        store.Get("k").Should().BeNull();
        store.Set("k", "v");
        store.Get("k").Should().Be("v");
        store.Delete("k");
        store.Get("k").Should().BeNull();
        store.Delete("missing"); // no throw
    }

    [Theory]
    [InlineData("cluster/prod-eu/sasl.password", "KAFKALENS_SECRET__CLUSTER__PROD_EU__SASL_PASSWORD")]
    [InlineData("cluster/3f2a/schema.registry.password", "KAFKALENS_SECRET__CLUSTER__3F2A__SCHEMA_REGISTRY_PASSWORD")]
    public void EnvironmentStore_MapsKeysToVariableNames(string key, string variable)
    {
        EnvironmentSecretStore.VariableName(key).Should().Be(variable);
    }

    [Fact]
    public void EnvironmentStore_ReadsVariables_AndIsReadOnly()
    {
        var vars = new Dictionary<string, string> { ["KAFKALENS_SECRET__CLUSTER__A__SASL_PASSWORD"] = "s3cret" };
        var store = new EnvironmentSecretStore(n => vars.GetValueOrDefault(n));

        store.Get("cluster/a/sasl.password").Should().Be("s3cret");
        store.Get("cluster/b/sasl.password").Should().BeNull();
        store.Invoking(s => s.Set("x", "y")).Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void ChainedStore_PrefersReadOnlySource_AndWritesToSecond()
    {
        var env = new EnvironmentSecretStore(n => n == "KAFKALENS_SECRET__A" ? "from-env" : null);
        var writable = new InMemorySecretStore();
        var store = new ChainedSecretStore(env, writable);

        writable.Set("a", "from-store");
        writable.Set("b", "only-store");
        store.Get("a").Should().Be("from-env");
        store.Get("b").Should().Be("only-store");

        store.Set("c", "x");
        writable.Get("c").Should().Be("x");
        store.IsPersistent.Should().BeFalse();
    }

    [Fact]
    public void Factory_AlwaysReturnsAStore()
    {
        // In CI there is usually no keyring; the factory must fall back rather than fail.
        var store = SecretStoreFactory.CreateDefault();
        store.Should().NotBeNull();
        store.BackendName.Should().NotBeNullOrWhiteSpace();
    }
}

public class SecretRedactorTests
{
    [Fact]
    public void Redact_ReplacesKnownSecretValues()
    {
        SecretRedactor.Redact("auth failed for user bob with p4ssw0rd!", ["p4ssw0rd!"])
            .Should().Be("auth failed for user bob with ********");
    }

    [Theory]
    [InlineData("sasl.password=abc123 sasl.username=bob", "sasl.password=******** sasl.username=bob")]
    [InlineData("ssl.key.password: \"pa ss\"", "ssl.key.password: ********")]
    [InlineData("basic.auth.user.info=key:secret", "basic.auth.user.info=********")]
    [InlineData("api_secret='xyz', other=1", "api_secret=********, other=1")]
    public void Redact_MasksCredentialAssignments(string input, string expected)
    {
        SecretRedactor.Redact(input).Should().Be(expected);
    }

    [Fact]
    public void Redact_LeavesOrdinaryTextAlone()
    {
        SecretRedactor.Redact("Broker: Unknown topic or partition").Should().Be("Broker: Unknown topic or partition");
        SecretRedactor.Redact(null).Should().BeEmpty();
    }
}
