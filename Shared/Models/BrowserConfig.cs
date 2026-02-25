using System;
using System.Collections.Generic;

namespace KafkaLens.Shared.Models;

public class BrowserConfig
{
    public int FontSize { get; set; } = 14;
    public int DefaultFetchCount { get; set; } = 10;
    public ISet<int> FetchCounts { get; set; } = new SortedSet<int>();
    public bool RestoreTabsOnStartup { get; set; } = true;
    public List<OpenedTabState> OpenedTabs { get; set; } = new List<OpenedTabState>();
}

public class OpenedTabState
{
    public string ClusterId { get; set; } = "";
    public string? SelectedNodeType { get; set; }
    public string? SelectedTopicName { get; set; }
    public int? SelectedPartitionId { get; set; }
    public string? FetchPosition { get; set; }
    public int FetchCount { get; set; }
    public bool FetchBackward { get; set; }
    public string? StartOffset { get; set; }
    public DateTime? StartDate { get; set; }
    public string? StartTimeText { get; set; }
    public string? MessagesSortColumn { get; set; }
    public bool? MessagesSortAscending { get; set; }
    public string PositiveFilter { get; set; } = "";
    public string NegativeFilter { get; set; } = "";
    public string LineFilter { get; set; } = "";
    public bool UseObjectFilter { get; set; } = true;
}
