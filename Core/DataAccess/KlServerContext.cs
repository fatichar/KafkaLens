using KafkaLens.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace KafkaLens.Core.DataAccess;

public class KlServerContext : DbContext
{
    public KlServerContext(DbContextOptions<KlServerContext> options)
        : base(options)
    {
    }

    public DbSet<KafkaCluster> KafkaClusters => Set<KafkaCluster>();

    // TODO: Add topic formats
}