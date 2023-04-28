using System.Collections.ObjectModel;
using KafkaLens.Shared.Entities;

namespace KafkaLens.Shared.DataAccess;

public interface IClustersRepository
{
    ReadOnlyDictionary<string, KafkaCluster> GetAll();
    KafkaCluster GetById(string id);
    void Add(KafkaCluster cluster);
    void Update(KafkaCluster cluster);
    void Delete(string id);
}