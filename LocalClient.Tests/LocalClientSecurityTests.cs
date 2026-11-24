using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KafkaLens.Core.Services;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Models;
using KafkaLens.Shared.Security;
using NSubstitute;
using Xunit;

namespace KafkaLens.Clients.Tests;

public sealed class LocalClientSecurityTests : System.IDisposable
{
    private readonly string file = Path.Combine(Path.GetTempPath(), $"kl-local-sec-{System.Guid.NewGuid():N}.json");
    public void Dispose() { if (File.Exists(file)) File.Delete(file); }

    private sealed class RecordingFactory : ConsumerFactory
    {
        public readonly List<KafkaConnectionSettings> Secured = new();
        public readonly List<string> Plain = new();
        public IKafkaConsumer Consumer { get; } = Substitute.For<IKafkaConsumer>();

        public RecordingFactory() : this(0) { }
        private RecordingFactory(int _) : base(new KafkaConfig()) => Consumer.GetTopics().Returns(new List<Topic>());

        public override IKafkaConsumer CreateNew(string url) { Plain.Add(url); return Consumer; }
        protected override IKafkaConsumer CreateSecured(KafkaConnectionSettings settings) { Secured.Add(settings); return Consumer; }
    }

    private static readonly ClusterSecurity MutualTls = new()
    {
        Protocol = KafkaSecurityProtocol.Ssl, SslCaLocation = "/ca.pem", SslCertificateLocation = "/c.pem", SslKeyLocation = "/c.key"
    };

    [Fact]
    public async Task Add_PersistsSecurity_AndUsesItForConsumers()
    {
        var repo = new ClusterInfoRepository(file, new InMemorySecretStore());
        var factory = new RecordingFactory();
        var sut = new LocalClient(repo, new KafkaConfig(), factory);

        var created = await sut.AddAsync(new NewKafkaCluster("secure", "broker:9093")
        {
            Security = MutualTls,
            Secrets = new() { [ClusterSecretNames.SslKeyPassword] = "pw" }
        });
        await sut.GetTopicsAsync(created.Id);

        created.Security!.Protocol.Should().Be(KafkaSecurityProtocol.Ssl);
        created.StoredSecretNames.Should().Contain(ClusterSecretNames.SslKeyPassword);
        factory.Secured.Should().ContainSingle().Which.GetSecret(ClusterSecretNames.SslKeyPassword).Should().Be("pw");
        File.ReadAllText(file).Should().Contain("sslCaLocation").And.NotContain("\"pw\"");
    }

    [Fact]
    public async Task ChangingSecurity_ReplacesCachedConsumer()
    {
        var repo = new ClusterInfoRepository(file, new InMemorySecretStore());
        var factory = new RecordingFactory();
        var sut = new LocalClient(repo, new KafkaConfig(), factory);
        var created = await sut.AddAsync(new NewKafkaCluster("c", "broker:9092"));
        await sut.GetTopicsAsync(created.Id);
        factory.Plain.Should().ContainSingle();

        await sut.UpdateClusterAsync(created.Id, new KafkaClusterUpdate("c", "broker:9092") { Security = MutualTls });
        await sut.GetTopicsAsync(created.Id);

        factory.Consumer.Received(1).Dispose();
        factory.Secured.Should().ContainSingle();
    }

    [Fact]
    public async Task ValidateWithSettings_UsesTemporarySecuredConsumer()
    {
        var factory = new RecordingFactory();
        factory.Consumer.ValidateConnectionWithDetailsAsync(Arg.Any<CancellationToken>())
            .Returns(ConnectionValidationResult.Success());
        var sut = new LocalClient(new ClusterInfoRepository(file), new KafkaConfig(), factory);

        var result = await sut.ValidateConnectionWithDetailsAsync(
            new KafkaConnectionSettings("broker:9093", MutualTls, new Dictionary<string, string>()), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        factory.Secured.Should().ContainSingle();
        factory.Consumer.Received(1).Dispose();
    }

    [Fact]
    public async Task ValidateWithSettings_RedactsSecretsFromErrors()
    {
        var factory = new RecordingFactory();
        factory.Consumer.ValidateConnectionWithDetailsAsync(Arg.Any<CancellationToken>())
            .Returns<Task<ConnectionValidationResult>>(_ => throw new System.InvalidOperationException("bad key password 'pw-123'"));
        var sut = new LocalClient(new ClusterInfoRepository(file), new KafkaConfig(), factory);

        var result = await sut.ValidateConnectionWithDetailsAsync(new KafkaConnectionSettings("broker:9093", MutualTls,
            new Dictionary<string, string> { [ClusterSecretNames.SslKeyPassword] = "pw-123" }), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().NotContain("pw-123");
        result.ErrorDetails.Should().NotContain("pw-123");
    }
}
