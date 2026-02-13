using KafkaLens.Shared;

namespace KafkaLens.ViewModels;

public interface IClientFactory
{
    Task LoadClientsAsync();
    List<IKafkaLensClient> GetAllClients();
    IKafkaLensClient GetClient(string clientId);
}