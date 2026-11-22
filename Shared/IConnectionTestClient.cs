using System.Threading;
using System.Threading.Tasks;
using KafkaLens.Shared.Models;

namespace KafkaLens.Shared;

public interface IConnectionTestClient
{
    Task<ConnectionValidationResult> ValidateConnectionWithDetailsAsync(string bootstrapServers);
}

public interface ICancellableConnectionClient
{
    Task<ConnectionValidationResult> ValidateConnectionWithDetailsAsync(string address, CancellationToken cancellationToken);
}
