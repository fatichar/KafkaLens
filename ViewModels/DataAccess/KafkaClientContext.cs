using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KafkaLens.ViewModels.DataAccess;
using KafkaLens.ViewModels.Entities;

public class KafkaClientContext : DbContext
{
    public KafkaClientContext(DbContextOptions<KafkaClientContext> options)
        : base(options)
    {
    }

    public DbSet<KafkaCluster> Clusters => Set<KafkaCluster>();
    public DbSet<KafkaLensClient> Clients => Set<KafkaLensClient>();

    // TODO: Add topic formats
}

public class KafkaContextFactory : IDesignTimeDbContextFactory<KafkaClientContext>
{
    public KafkaClientContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<KafkaClientContext>();
        optionsBuilder.UseSqlite("Data Source=KafkaLensApp.db;");

        return new KafkaClientContext(optionsBuilder.Options);
    }
}