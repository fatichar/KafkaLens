using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KafkaLens.Server.DataAccess
{
    public class KafkaClusterSqlRepository : IKafkaClusterRepository
    {
        public KafkaCluster Add(KafkaCluster cluster)
        {
            throw new NotImplementedException();
        }

        public KafkaCluster GetById(string id)
        {
            throw new NotImplementedException();
        }

        public KafkaCluster GetByName(string name)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<KafkaCluster> GetKafkaClusters()
        {
            throw new NotImplementedException();
        }

        public KafkaCluster RemoveById(string id)
        {
            throw new NotImplementedException();
        }

        public KafkaCluster Update(KafkaCluster updated)
        {
            throw new NotImplementedException();
        }
    }
}
