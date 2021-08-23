﻿using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KafkaLens.Server.Entities
{
    [Table(name:"cluster")]
    public class KafkaCluster
    {
        public KafkaCluster(string id, string name, string bootstrapServers)
        {
            Id = id;
            Name = name;
            BootstrapServers = bootstrapServers;
        }

        [Required]
        public string Id { get; set; }
        public string Name { get; set; }
        public string BootstrapServers { get; set; }
    }
}