﻿// <auto-generated />
using KafkaLens.Server.DataAccess;
using KafkaLens.Server.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace KafkaLens.Server.Migrations
{
    [DbContext(typeof(KafkaContext))]
    partial class KafkaContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "5.0.8");

            modelBuilder.Entity("KafkaLens.Server.Entities.KafkaCluster", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("TEXT");

                    b.Property<string>("BootstrapServers")
                        .HasColumnType("TEXT");

                    b.Property<string>("Name")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("cluster");
                });
#pragma warning restore 612, 618
        }
    }
}