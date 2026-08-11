using KafkaLens.Clients;
using KafkaLens.Clients.Entities;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Models;
using KafkaLens.ViewModels.Services;
using Serilog;

namespace KafkaLens.ViewModels;

public class ClientFactory : IClientFactory
{
    private const string HTTP_PROTOCOL_PREFIX = "http://";

    private readonly IClientInfoRepository infoRepository;
    private readonly IAppLogService? appLogService;

    private readonly IDictionary<string, IKafkaLensClient> clients = new Dictionary<string, IKafkaLensClient>();

    public ClientFactory(IClientInfoRepository infoRepository, IKafkaLensClient localClient, IAppLogService? appLogService = null)
    {
        this.infoRepository = infoRepository;
        this.appLogService = appLogService;
        clients.Add(localClient.Name, localClient);
    }

    public ClientFactory(IClientInfoRepository infoRepository, IAppLogService? appLogService = null)
    {
        this.infoRepository = infoRepository;
        this.appLogService = appLogService;
    }

    public Task LoadClientsAsync()
    {
        var clientInfos = infoRepository.GetAll();
        foreach (var clientInfosKey in clientInfos.Values)
        {
            Log.Information("Found client: {ClientName} in config", clientInfosKey.Name);
        }

        var toRemove = clients.Keys
            .Where(k => clientInfos.Values.All(ci => ci.Name != k) && k != "Local")
            .ToList();

        foreach (var key in toRemove)
        {
            clients.Remove(key);
        }

        foreach (var clientInfo in clientInfos.Values)
        {
            Log.Information("Loading client: {ClientName}", clientInfo.Name);
            try
            {
                clients.Remove(clientInfo.Name);
                var client = CreateClient(clientInfo);
                clients.Add(client.Name, client);
            }
            catch (Exception)
            {
                Log.Error("Failed to load client {}", clientInfo.Name);
                appLogService?.LogError($"Could not load client {clientInfo.Name}", "Startup");
            }
        }

        return Task.CompletedTask;
    }

    public async Task<IEnumerable<KafkaCluster>> TestConnectionAsync(ClientInfo clientInfo)
    {
        IKafkaLensClient? client = null;
        try
        {
            client = CreateClient(clientInfo);
            return await client.GetAllClustersAsync();
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to test client connection for {ClientName}", clientInfo.Name);
            return new[]
            {
                new KafkaCluster($"client-unavailable:{clientInfo.Address}", clientInfo.Name, clientInfo.Address)
                {
                    Status = ConnectionState.Failed,
                    LastError = e.Message
                }
            };
        }
        finally
        {
            if (client is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    public List<IKafkaLensClient> GetAllClients()
    {
        return clients.Values.ToList();
    }

    public IKafkaLensClient GetClient(string clientId)
    {
        if (clients.TryGetValue(clientId, out var client))
        {
            return client;
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
