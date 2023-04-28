using System.Collections.ObjectModel;
using System.Text.Json;
using KafkaLens.Clients.Entities;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Entities;
using Serilog;

namespace KafkaLens.Core.DataAccess;

public class ClientsRepository : IClientsRepository
{
    private const string DEFAULT_FILE_PATH = "client_info.json";
    private readonly string filePath;
    private Dictionary<string, KafkaLensClient> clients;
    public ReadOnlyDictionary<string, KafkaLensClient> GetAll() => new(clients);

    public ClientsRepository(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            Log.Warning("Clients config file path not specified, using default: {DefaultPath}",
                DEFAULT_FILE_PATH);
            filePath = DEFAULT_FILE_PATH;
            File.CreateText(filePath);
        }
        this.filePath = filePath;

        LoadClients(filePath);
    }

    private void LoadClients(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new IOException($"File {filePath} does not exist");
        }

        var configFile = File.ReadAllText(filePath);
        var clientConfig = JsonSerializer.Deserialize<ClientConfig>(configFile);
        clients = clientConfig.Clients.ToDictionary(client => client.Id);
    }

    public KafkaLensClient GetById(string id)
    {
        ValidateClientById(id);
        return clients[id];
    }

    private void ValidateClientById(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new Exception("Client id cannot be null or empty");
        }
        if (!clients.ContainsKey(id))
        {
            throw new Exception($"Client with id {id} does not exist");
        }
    }

    public void Add(KafkaLensClient client)
    {
        if (clients.ContainsKey(client.Id))
        {
            throw new Exception($"Client with id {client.Id} already exists");
        }

        clients.Add(client.Id, client);
        SaveClients();
    }

    private void SaveClients()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        var clientConfig = new ClientConfig
        {
            Clients = clients.Values.ToList()
        };
        var json = JsonSerializer.Serialize(clientConfig, options);
        File.WriteAllText(filePath, json);
    }

    public void Update(KafkaLensClient client)
    {
        ValidateClientById(client.Id);
        clients[client.Id] = client;
        SaveClients();
    }
    
    public void Delete(string id)
    {
        ValidateClientById(id);
        clients.Remove(id);
        SaveClients();
    }
}