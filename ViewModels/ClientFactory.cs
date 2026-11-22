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
    private readonly Dictionary<string, (string Address, string Protocol)> clientTransports = new(StringComparer.Ordinal);
    private readonly HashSet<string> disabledClients = new(StringComparer.Ordinal);
    private readonly IKafkaLensClient? localClient;

    public ClientFactory(IClientInfoRepository infoRepository, IKafkaLensClient localClient, IAppLogService? appLogService = null)
    {
        this.infoRepository = infoRepository;
        this.appLogService = appLogService;
        this.localClient = localClient;
        clients.Add(localClient.Name, localClient);
    }

    public ClientFactory(IClientInfoRepository infoRepository, IAppLogService? appLogService = null)
    {
        this.infoRepository = infoRepository;
        this.appLogService = appLogService;
    }

    public Task LoadClientsAsync()
    {
        var configured = infoRepository.GetAll().Values.Where(c => c.Name != localClient?.Name).ToArray();
        disabledClients.Clear();
        foreach (var info in configured.Where(c => !c.IsEnabled)) disabledClients.Add(info.Name);
        foreach (var key in clients.Keys.Where(k => k != localClient?.Name && configured.All(c => c.Name != k)).ToArray())
        {
            if (clients[key] is IDisposable disposable) disposable.Dispose();
            clients.Remove(key);
            clientTransports.Remove(key);
        }
        foreach (var info in configured)
        {
            var transport = (info.Address, info.Protocol);
            if (clientTransports.TryGetValue(info.Name, out var existing) && existing == transport) continue;
            try
            {
                var client = CreateClient(info);
                if (clients.TryGetValue(info.Name, out var previous) && previous is IDisposable disposable)
                    disposable.Dispose();
                clients[info.Name] = client;
                clientTransports[info.Name] = transport;
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to load client {ClientName}", info.Name);
                appLogService?.LogError($"Could not load client {info.Name}", "Startup");
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
                    LastError = e.Message,
                    IsUnavailablePlaceholder = true
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
        return clients.Values.Where(c => !disabledClients.Contains(c.Name)).ToList();
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
