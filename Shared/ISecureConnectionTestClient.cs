using System.Threading;
using System.Threading.Tasks;
using KafkaLens.Shared.Models;

namespace KafkaLens.Shared;

/// <summary>
/// Tests a connection described by full settings (TLS, authentication, secrets) before it is saved,
/// e.g. from the Add/Edit Cluster dialog.
/// </summary>
public interface ISecureConnectionTestClient
{
    Task<ConnectionValidationResult> ValidateConnectionWithDetailsAsync(KafkaConnectionSettings settings, CancellationToken cancellationToken);
}
