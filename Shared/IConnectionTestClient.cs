using System.Threading.Tasks;
using KafkaLens.Shared.Models;

namespace KafkaLens.Shared;

public interface IConnectionTestClient
{
    Task<ConnectionValidationResult> ValidateConnectionWithDetailsAsync(string bootstrapServers);
}
