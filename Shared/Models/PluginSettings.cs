using System.Collections.Generic;

namespace KafkaLens.Shared.Models;

public class PluginSettings
{
    public List<string> Repositories { get; set; } = [];

    public Dictionary<string, bool> PluginStates { get; set; } = [];
}