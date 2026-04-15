using System.Collections.Generic;

namespace KafkaLens.Shared.Models;

public class PluginSettings
{
    public List<string> Repositories { get; set; } = ["https://fatichar.github.io/kafkalens-plugin-index/plugins.json"];

    public Dictionary<string, bool> PluginStates { get; set; } = [];
}