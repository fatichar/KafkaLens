using KafkaLens.Clients.Entities;
using KafkaLens.Shared;
using KafkaLens.Shared.Models;

namespace KafkaLens.ViewModels;

public interface IClientFactory
{
    Task LoadClientsAsync();
    Task<IEnumerable<KafkaCluster>> TestConnectionAsync(ClientInfo clientInfo);
    List<IKafkaLensClient> GetAllClients();
    IKafkaLensClient GetClient(string clientId);
}