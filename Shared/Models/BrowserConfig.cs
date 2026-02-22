using System.Collections.Generic;

namespace KafkaLens.Shared.Models;

public class BrowserConfig
{
    public int FontSize { get; set; } = 14;
    public int DefaultFetchCount { get; set; } = 10;
    public List<int> FetchCounts { get; set; } = new List<int> { 10, 25, 50, 100, 250, 500, 1000, 5000, 10000, 25000 };
    public bool RestoreTabsOnStartup { get; set; } = true;
    public List<string> OpenedClusterIds { get; set; } = new List<string>();
}
