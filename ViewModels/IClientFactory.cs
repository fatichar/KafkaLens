using KafkaLens.Clients.Entities;
using KafkaLens.Shared;
using KafkaLens.Shared.Entities;

namespace KafkaLens.ViewModels;

public interface IClientFactory
{
    Task LoadClientsAsync();
    List<IKafkaLensClient> GetAllClients();
    IKafkaLensClient GetClient(string clientId);
}