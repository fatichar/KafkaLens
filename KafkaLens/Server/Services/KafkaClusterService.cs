using KafkaLens.Server.DataAccess;
using KafkaLens.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KafkaLens.Server.Services
{
    public class KafkaClusterService
    {
        private readonly ILogger<KafkaClusterService> _logger;
        private readonly KafkaContext _dbContext;
        public DbSet<Entities.KafkaCluster> Clusters => _dbContext.KafkaClusters;

        public KafkaClusterService(ILogger<KafkaClusterService> logger, KafkaContext dbContext)
        {
            _logger = logger;
            _dbContext = dbContext;
        }

        internal KafkaCluster Add(NewKafkaCluster newCluster)
        {
            Validate(newCluster);

            var cluster = CreateCluster(newCluster);

            Clusters.Add(cluster);

            _dbContext.SaveChangesAsync();
            
            return ToModel(cluster);
        }

        private static Entities.KafkaCluster CreateCluster(NewKafkaCluster newCluster)
        {
            var cluster = new Entities.KafkaCluster(Guid.NewGuid().ToString(), newCluster.Name, newCluster.BootstrapServers);

            return cluster;
        }

        internal IEnumerable<KafkaCluster> GetAllClusters()
        {
            Log.Information("Get all clusters");
            return Clusters.Select(ToModel);
        }

        internal async Task<KafkaCluster> GetByIdAsync(string id)
        {
            var item = await Clusters.FirstOrDefaultAsync(cluster => cluster.Id == id);
            return item == null ? null : ToModel(item);
        }

        #region Validations
        private void Validate(NewKafkaCluster newCluster)
        {
            var all = Clusters.ToList();

            var existing = Clusters.FirstOrDefault(cluster => cluster.Name.Equals(newCluster.Name));

            if (existing != null)
            {
                throw new ArgumentException($"Cluster with name {existing.Name} already exists");
            }
        }
        #endregion Validations

        #region Mappers
        private KafkaCluster ToModel(Entities.KafkaCluster cluster)
        {
            return new KafkaCluster(cluster.Id, cluster.Name, cluster.BootstrapServers);
        }

        private Entities.KafkaCluster ToEntity(KafkaCluster cluster)
        {
            return new Entities.KafkaCluster(cluster.Id, cluster.Name, cluster.BootstrapServers);
        }
        #endregion Mappers
    }
}
