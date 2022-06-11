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
        public ClusterService ClusterService { get; private set; }
        public KafkaContext DbContext { get; private set; }

        public ClusterServiceTests()
        {
            Fixture fixture = new();
            fixture.Register(() => Substitute.For<ILogger<ClusterService>>());

            var builder = new DbContextOptionsBuilder<KafkaContext>();
            builder.UseSqlite("Data Source = KafkaLens.Core.UnitTest.db");
            DbContext = new KafkaContext(builder.Options);
            DbContext.Database.EnsureDeleted();
            DbContext.Database.EnsureCreated();
            fixture.Register(() => DbContext);
            ClusterService = fixture.Create<ClusterService>();
        }


        [Fact()]
        public async void AddAsync_validCluster_added()
        {
            // arrange
            var oldCount = ClusterService.GetAllClusters().Count();
            
            // act
            var addedCluster = await ClusterService.AddAsync(new NewKafkaCluster("Dev", "localhost:9092"));

            // assert
            var clusters = ClusterService.GetAllClusters();
            Assert.Equal(oldCount + 1, clusters.Count());
            Assert.Equal(addedCluster, clusters.Last());
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