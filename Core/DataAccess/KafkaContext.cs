using KafkaLens.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace KafkaLens.Core.DataAccess
{
    public class KafkaContext : DbContext
    {
        public KafkaContext(DbContextOptions<KafkaContext> options)
            : base(options)
        {
        }

        public DbSet<KafkaCluster> KafkaClusters => Set<KafkaCluster>();

        // TODO: Add topic formats
    }
}
