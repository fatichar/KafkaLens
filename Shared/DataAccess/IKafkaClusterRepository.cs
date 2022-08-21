using KafkaLens.Shared.Models;
using System.Collections.Generic;

namespace KafkaLens.Shared.DataAccess
{
    public interface IKafkaClusterRepository
    {
        KafkaCluster Add(KafkaCluster cluster);
        IEnumerable<KafkaCluster> GetKafkaClusters();
        KafkaCluster GetById(string id);
        KafkaCluster GetByName(string name);
        KafkaCluster Update(KafkaCluster updated);
        KafkaCluster RemoveById(string id);
    }
}
