using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using KafkaLens.Clients.Entities;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Entities;
using Serilog;

namespace KafkaLens.Core.DataAccess;

public class ClientInfoRepository : IClientInfoRepository
{
    private const string DEFAULT_FILE_PATH = "client_info.json";
    private readonly string filePath;
    private Dictionary<string, ClientInfo> clients;
    public ReadOnlyDictionary<string, ClientInfo> GetAll() => new(clients);

    public ClientInfoRepository(string filePath)
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

    public ClientInfo GetById(string id)
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

    public void Add(ClientInfo clientInfo)
    {
        if (clients.ContainsKey(clientInfo.Id))
        {
            throw new Exception($"Client with id {clientInfo.Id} already exists");
        }

        clients.Add(clientInfo.Id, clientInfo);
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

    public void Update(ClientInfo clientInfo)
    {
        ValidateClientById(clientInfo.Id);
        clients[clientInfo.Id] = clientInfo;
        SaveClients();
    }
    
    public void Delete(string id)
    {
        ValidateClientById(id);
        clients.Remove(id);
        SaveClients();
    }
}