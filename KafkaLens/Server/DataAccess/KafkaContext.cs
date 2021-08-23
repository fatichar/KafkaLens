using KafkaLens.Server.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KafkaLens.Server.DataAccess
{
    public class KafkaContext : DbContext
    {
        public KafkaContext(DbContextOptions<KafkaContext> options)
            : base(options)
        {
        }

        public DbSet<KafkaCluster> KafkaClusters { get; set; }
    }
}
