using System.Collections.ObjectModel;
using KafkaLens.Clients.Entities;

namespace KafkaLens.Shared.DataAccess;

public interface IClientInfoRepository
{
    ReadOnlyDictionary<string, ClientInfo> GetAll();
    ClientInfo GetById(string id);
    void Add(ClientInfo cluster);
    void Update(ClientInfo cluster);
    void Delete(string id);
}