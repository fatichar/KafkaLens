using Xunit;
using KafkaLens.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using KafkaLens.Core.DataAccess;
using KafkaLens.Shared.Models;
using NSubstitute;
using AutoFixture;
using Microsoft.EntityFrameworkCore;

namespace KafkaLens.Core.Services
{
    public class ClusterServiceTests
    {
        ILogger<ClusterService> logger = Substitute.For<ILogger<ClusterService>>();

        [Fact]
        public async void AddAsyncTest()
        {
            Fixture fixture = new();
            fixture.Register(() => Substitute.For<ILogger<ClusterService>>());
            //fixture.Register(() => Substitute.For<IServiceScopeFactory>());

            var builder = new DbContextOptionsBuilder<KafkaContext>();
            builder.UseSqlite("Data Source = KafkaLens.Core.UnitTest.db");
            var dbContext = new KafkaContext(builder.Options);
            dbContext.Database.EnsureDeleted();
            dbContext.Database.EnsureCreated();
            fixture.Register(() => dbContext);

            var clustersService = fixture.Create<ClusterService>();
            var addedCluster = await clustersService.AddAsync(new NewKafkaCluster("Dev", "localhost:9092"));
        }

        [Fact()]
        public void GetTopicsAsync__success()
        {
            Assert.True(false, "This test needs an implementation");
        }

        [Fact()]
        public void GetMessagesAsync__success()
        {
            Assert.True(false, "This test needs an implementation");
        }

        [Fact()]
        public void GetMessagesAsync__success1()
        {
            Assert.True(false, "This test needs an implementation");
        }
    }
}