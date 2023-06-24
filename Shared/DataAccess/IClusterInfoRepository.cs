using System.Collections.Generic;
using System.Collections.ObjectModel;
using KafkaLens.Shared.Entities;

namespace KafkaLens.Shared.DataAccess;

public interface IClusterInfoRepository
{
    ReadOnlyDictionary<string, ClusterInfo> GetAll();
    ClusterInfo GetById(string id);
    ClusterInfo Add(string name, string address);
    void Add(ClusterInfo clusterInfo);
    void AddAll(IEnumerable<ClusterInfo> clusterInfos);
    void Update(ClusterInfo clusterInfo);
    void Delete(string id);
    void DeleteAll();
}