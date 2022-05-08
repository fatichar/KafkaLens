using KafkaLens.Core.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KafkaLens.Core.DataAccess
{
    public class KafkaContext : DbContext
    {
        public KafkaContext(DbContextOptions<KafkaContext> options)
            : base(options)
        {
        }

        public DbSet<KafkaCluster> KafkaClusters => Set<KafkaCluster>();
    }
}
