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
    public class ClustersService
    {
        private readonly ILogger<ClustersService> _logger;
        private readonly KafkaContext _dbContext;
        public DbSet<Entities.KafkaCluster> Clusters => _dbContext.KafkaClusters;

        public ClustersService(ILogger<ClustersService> logger, KafkaContext dbContext)
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
            var cluster = await Validate(id);
            return ToModel(cluster);
        }

        internal async Task<KafkaCluster> RemoveByIdAsync(string id)
        {
            var cluster = await Validate(id);
            Clusters.Remove(cluster);
            await _dbContext.SaveChangesAsync();
            return ToModel(cluster);
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

        private async Task<Entities.KafkaCluster> Validate(string id)
        {
            var cluster = await Clusters.FindAsync(id);
            if (cluster == null)
            {
                throw new ArgumentException("", nameof(id));
            }
            return cluster;
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
