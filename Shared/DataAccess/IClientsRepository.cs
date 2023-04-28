using System.Collections.ObjectModel;
using KafkaLens.Clients.Entities;

namespace KafkaLens.Shared.DataAccess;

public interface IClientsRepository
{
    ReadOnlyDictionary<string, KafkaLensClient> GetAll();
    KafkaLensClient GetById(string id);
    void Add(KafkaLensClient cluster);
    void Update(KafkaLensClient cluster);
    void Delete(string id);
}