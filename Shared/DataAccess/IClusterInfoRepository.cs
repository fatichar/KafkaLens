using System.Collections.ObjectModel;
using KafkaLens.Shared.Entities;

namespace KafkaLens.Shared.DataAccess;

public interface IClusterInfoRepository
{
    ReadOnlyDictionary<string, ClusterInfo> GetAll();
    ClusterInfo GetById(string id);
    void Add(ClusterInfo clusterInfo);
    void Update(ClusterInfo clusterInfo);
    void Delete(string id);
}