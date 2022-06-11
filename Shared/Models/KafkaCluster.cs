﻿using System;

namespace KafkaLens.Shared.Models
{
    public class KafkaCluster
    {
        public KafkaCluster(string id, string name, string bootstrapServers)
        {
            Id = id;
            Name = name;
            BootstrapServers = bootstrapServers;
        }

        public string Id { get; set; }
        public string Name { get; set; }
        public string BootstrapServers { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is KafkaCluster cluster)
            {
                return cluster.Id.Equals(Id, StringComparison.CurrentCultureIgnoreCase);
            }
            return false;
        }
    }
}