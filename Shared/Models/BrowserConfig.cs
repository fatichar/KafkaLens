using System.Collections.Generic;

namespace KafkaLens.Shared.Models;

public class BrowserConfig
{
    public int FontSize { get; set; } = 14;
    public int DefaultFetchCount { get; set; } = 10;
    public ISet<int> FetchCounts { get; set; } = new SortedSet<int>();
    public bool RestoreTabsOnStartup { get; set; } = true;
    public List<string> OpenedClusterIds { get; set; } = new List<string>();
}