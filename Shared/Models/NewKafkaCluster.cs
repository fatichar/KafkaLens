using System.Collections.Generic;

namespace KafkaLens.Shared.Models;

public class NewKafkaCluster(string name, string address)
{
    public string Name { get; set; } = name;
    public string Address { get; set; } = address;

    public ClusterSecurity? Security { get; set; }

    /// <summary>
    /// Secrets to store, by <see cref="Entities.ClusterSecretNames"/> name. Null leaves stored secrets unchanged;
    /// secrets not applicable to <see cref="Security"/> are removed.
    /// </summary>
    public Dictionary<string, string>? Secrets { get; set; }
}