using KafkaLens.Clients;
using KafkaLens.Clients.Entities;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using Serilog;

namespace KafkaLens.ViewModels;

public class ClientFactory : IClientFactory
{
    private const string HTTP_PROTOCOL_PREFIX = "http://";
    
    private readonly IClientInfoRepository infoRepository;
    
    private readonly IDictionary<string, IKafkaLensClient> clients = new Dictionary<string, IKafkaLensClient>();

    public ClientFactory(IClientInfoRepository infoRepository, IKafkaLensClient localClient)
    {
        this.infoRepository = infoRepository;
        clients.Add(localClient.Name, localClient);
    }

    public ClientFactory(IClientInfoRepository infoRepository)
    {
        this.infoRepository = infoRepository;
    }

    public async Task LoadClientsAsync()
    {
        var clientInfos = infoRepository.GetAll();
        foreach (var clientInfosKey in clientInfos.Values)
        {
            Log.Information("Found client: {ClientName} in config", clientInfosKey.Name);
        }

        foreach (var clientInfo in clientInfos.Values)
        {
            Log.Information("Loading client: {ClientName}", clientInfo.Name);
            try
            {
                var client = CreateClient(clientInfo);
                clients.Add(client.Name, client);
            }
            catch (Exception e)
            {
                Log.Error("Failed to load client {}", clientInfo.Name);
            }
        }
    }

    public List<IKafkaLensClient> GetAllClients()
    {
        return clients.Values.ToList();
    }

    public IKafkaLensClient GetClient(string clientId)
    {
        if (clients.ContainsKey(clientId))
        {
            return clients[clientId];
        }
        throw new ArgumentException($"Client with Id {clientId} not found");
    }

    private static IKafkaLensClient CreateClient(ClientInfo clusterInfo)
    {
        switch (clusterInfo.Protocol)
        {
            case "grpc":
            {
                var address = SanitizeAddress(clusterInfo.Address);
                return new GrpcClient(clusterInfo.Name, address);
            }
            default:
                throw new ArgumentException($"Protocol {clusterInfo.Protocol} is not supported");
        }
    }

    private static string SanitizeAddress(string address)
    {
        if (!address.StartsWith(HTTP_PROTOCOL_PREFIX))
        {
            address = HTTP_PROTOCOL_PREFIX + address;
        }
        return address;
    }
}