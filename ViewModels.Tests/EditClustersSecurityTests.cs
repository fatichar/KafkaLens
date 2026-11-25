using System.IO;
using System.Threading;
using System.Collections.ObjectModel;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using KafkaLens.Clients.Entities;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Models;
using KafkaLens.Shared.Security;
using NSubstitute;
using Xunit;

namespace KafkaLens.ViewModels.Tests;

public sealed class EditClustersSecurityTests : IDisposable
{
    private readonly string file = Path.Combine(Path.GetTempPath(), $"kl-vm-sec-{Guid.NewGuid():N}.json");
    private readonly InMemorySecretStore store = new();
    private readonly ClusterInfoRepository repo;
    private readonly IKafkaLensClient local = Substitute.For<IKafkaLensClient, ISecureConnectionTestClient, IConnectionTestClient>();
    private readonly IClientFactory factory = Substitute.For<IClientFactory>();
    private readonly IClientInfoRepository clientRepo = Substitute.For<IClientInfoRepository>();

    public EditClustersSecurityTests()
    {
        repo = new ClusterInfoRepository(file, store);
        local.Name.Returns("Local");
        local.CanEditClusters.Returns(true);
        factory.GetClient("Local").Returns(local);
        factory.GetAllClients().Returns([local]);
        clientRepo.GetAll().Returns(new ReadOnlyDictionary<string, ClientInfo>(new Dictionary<string, ClientInfo>()));
    }

    public void Dispose() { if (File.Exists(file)) File.Delete(file); }

    private EditClustersViewModel NewDialog(ObservableCollection<ClusterViewModel> clusters) =>
        new(clusters, repo, clientRepo, factory, _ => Task.CompletedTask);

    private static ClusterSecurity MutualTls() => new()
    {
        Protocol = KafkaSecurityProtocol.Ssl, SslCertificateLocation = "/c.pem", SslKeyLocation = "/c.key"
    };

    [AvaloniaFact]
    public async Task AddWithTls_PersistsSettingsAndSecret()
    {
        var clusters = new ObservableCollection<ClusterViewModel>();
        using var dialog = NewDialog(clusters);

        await dialog.StageAddClusterAsync("secure", "b:9093", MutualTls(), new() { [ClusterSecretNames.SslKeyPassword] = "pw" });
        await dialog.SaveAsync();

        var saved = repo.GetAll().Values.Single();
        saved.Security!.Protocol.Should().Be(KafkaSecurityProtocol.Ssl);
        saved.GetSecret(ClusterSecretNames.SslKeyPassword).Should().Be("pw");
        clusters.Single().SecuritySummary.Should().Be("Mutual TLS");
        clusters.Single().StoredSecretNames.Should().Contain(ClusterSecretNames.SslKeyPassword);
        File.ReadAllText(file).Should().NotContain("\"pw\"");
    }

    [AvaloniaFact]
    public async Task EditWithoutRetypingPassword_KeepsIt_AndSwitchingToPlaintextRemovesIt()
    {
        var clusters = new ObservableCollection<ClusterViewModel>();
        using (var add = NewDialog(clusters))
        {
            await add.StageAddClusterAsync("secure", "b:9093", MutualTls(), new() { [ClusterSecretNames.SslKeyPassword] = "pw" });
            await add.SaveAsync();
        }
        var id = clusters.Single().Id;

        using (var rename = NewDialog(clusters))
        {
            var row = rename.Clusters.Single();
            await rename.StageUpdateCluster(row, "renamed", "b:9093", MutualTls(), new());
            await rename.SaveAsync();
        }
        repo.GetById(id).GetSecret(ClusterSecretNames.SslKeyPassword).Should().Be("pw");

        using (var plain = NewDialog(clusters))
        {
            var row = plain.Clusters.Single();
            await plain.StageUpdateCluster(row, "renamed", "b:9092", new ClusterSecurity());
            await plain.SaveAsync();
        }
        repo.GetById(id).Security.Should().BeNull();
        repo.GetById(id).Secrets.Should().BeEmpty();
        store.Get(SecretKeys.ForCluster(id, ClusterSecretNames.SslKeyPassword)).Should().BeNull();
    }

    [AvaloniaFact]
    public async Task TestConnection_UsesEnteredSettingsAndStoredSecrets()
    {
        var clusters = new ObservableCollection<ClusterViewModel>();
        using (var add = NewDialog(clusters))
        {
            await add.StageAddClusterAsync("secure", "b:9093", MutualTls(), new() { [ClusterSecretNames.SslKeyPassword] = "pw" });
            await add.SaveAsync();
        }
        KafkaConnectionSettings? tested = null;
        ((ISecureConnectionTestClient)local).ValidateConnectionWithDetailsAsync(Arg.Do<KafkaConnectionSettings>(s => tested = s), Arg.Any<CancellationToken>())
            .Returns(ConnectionValidationResult.Success());

        using var dialog = NewDialog(clusters);
        var row = dialog.Clusters.Single();
        var settings = dialog.BuildTestSettings(row, "b:9093", MutualTls(), new Dictionary<string, string>());
        var result = await dialog.TestConnectionAsync(row, settings);

        result.Succeeded.Should().BeTrue();
        tested!.GetSecret(ClusterSecretNames.SslKeyPassword).Should().Be("pw");
        tested.Security.SslKeyLocation.Should().Be("/c.key");
    }

    [AvaloniaFact]
    public async Task Sasl_StoresPasswordInStore_AndKeepsUsernameInSettings()
    {
        var clusters = new ObservableCollection<ClusterViewModel>();
        using var dialog = NewDialog(clusters);
        var sasl = new ClusterSecurity { Protocol = KafkaSecurityProtocol.SaslSsl, SaslMechanism = KafkaSaslMechanism.ScramSha512, SaslUsername = "alice" };

        await dialog.StageAddClusterAsync("auth", "b:9093", sasl, new() { [ClusterSecretNames.SaslPassword] = "s3cr3t" });
        await dialog.SaveAsync();

        var saved = repo.GetAll().Values.Single();
        saved.Security!.SaslUsername.Should().Be("alice");
        saved.GetSecret(ClusterSecretNames.SaslPassword).Should().Be("s3cr3t");
        File.ReadAllText(file).Should().Contain("alice").And.NotContain("s3cr3t");
        clusters.Single().SecuritySummary.Should().Be("SASL/SCRAM-SHA-512 + TLS");
    }

    [AvaloniaFact]
    public async Task TestingUnsavedPassword_DoesNotChangeLiveStatus()
    {
        var clusters = new ObservableCollection<ClusterViewModel>();
        using (var add = NewDialog(clusters))
        {
            await add.StageAddClusterAsync("secure", "b:9093", MutualTls(), new() { [ClusterSecretNames.SslKeyPassword] = "pw" });
            await add.SaveAsync();
        }
        var live = clusters.Single();
        live.ApplyDiagnosticStatus(ConnectionState.Connected);
        ((ISecureConnectionTestClient)local).ValidateConnectionWithDetailsAsync(Arg.Any<KafkaConnectionSettings>(), Arg.Any<CancellationToken>())
            .Returns(ConnectionValidationResult.Failed("bad key password"));

        using var dialog = NewDialog(clusters);
        var row = dialog.Clusters.Single();
        var settings = dialog.BuildTestSettings(row, "b:9093", MutualTls(), new Dictionary<string, string> { [ClusterSecretNames.SslKeyPassword] = "wrong" });
        var result = await dialog.TestConnectionAsync(row, settings);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        result.Succeeded.Should().BeFalse();
        live.Status.Should().Be(ConnectionState.Connected);
    }

    [AvaloniaFact]
    public async Task StagedButUnsavedPassword_CountsAsAvailable()
    {
        var clusters = new ObservableCollection<ClusterViewModel>();
        using var dialog = NewDialog(clusters);
        var sasl = new ClusterSecurity { Protocol = KafkaSecurityProtocol.SaslSsl, SaslUsername = "alice" };

        await dialog.StageAddClusterAsync("auth", "b:9093", sasl, new() { [ClusterSecretNames.SaslPassword] = "pw" });

        dialog.AvailableSecretNames(dialog.Clusters.Single()).Should().Contain(ClusterSecretNames.SaslPassword);
    }
}
