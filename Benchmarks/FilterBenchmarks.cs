using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BenchmarkDotNet.Attributes;
using KafkaLens.Formatting;
using KafkaLens.Shared.Models;
using KafkaLens.ViewModels;

namespace Benchmarks;

/// <summary>
/// Measures the cost of filtering the topic list – the exact algorithm used by
/// <c>OpenedClusterViewModel.FilterTopics()</c> – against <see cref="TopicCount"/>
/// topics and various filter patterns.
///
/// Uses <see cref="TopicViewModel"/> and <see cref="ObservableCollection{T}"/> directly
/// (no Avalonia dispatcher required) so results are stable in any environment.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class FilterBenchmarks
{
    [Params(10, 100, 500, 2000)]
    public int TopicCount;

    private List<TopicViewModel> _topics = null!;
    private ObservableCollection<ITreeNode> _children = null!;

    [GlobalSetup]
    public void Setup()
    {
        // FormatterFactory must be initialised before constructing TopicViewModels.
        FormatterFactory.AddFromPath("");

        _topics = Enumerable.Range(0, TopicCount)
            .Select(i =>
            {
                // Spread names across several realistic categories so partial-match
                // benchmarks exercise the Contains path with varying hit rates.
                var category = i % 5 switch
                {
                    0 => "orders",
                    1 => "payments",
                    2 => "inventory",
                    3 => "notifications",
                    _ => "analytics",
                };
                var topic = new Topic($"{category}.events.v{i / 5:D4}", partitionCount: 3);
                return new TopicViewModel(topic, formatterName: null, keyFormatterName: null);
            })
            .ToList();

        _children = new ObservableCollection<ITreeNode>();
    }

    // ── No filter (show all) ──────────────────────────────────────────────────

    [Benchmark(Description = "Filter – empty string (show all)")]
    public void Filter_Empty()
    {
        _children.Clear();
        const string filter = "";
        foreach (var topic in _topics)
        {
            if (string.IsNullOrWhiteSpace(filter) ||
                topic.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                _children.Add(topic);
        }
    }

    // ── Partial match (≈20 % of topics pass) ─────────────────────────────────

    [Benchmark(Description = "Filter – partial match (\"orders\")")]
    public void Filter_PartialMatch()
    {
        _children.Clear();
        const string filter = "orders";
        foreach (var topic in _topics)
        {
            if (string.IsNullOrWhiteSpace(filter) ||
                topic.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                _children.Add(topic);
        }
    }

    // ── No match (0 results) ──────────────────────────────────────────────────

    [Benchmark(Description = "Filter – no match (\"zzz-no-match\")")]
    public void Filter_NoMatch()
    {
        _children.Clear();
        const string filter = "zzz-no-match";
        foreach (var topic in _topics)
        {
            if (string.IsNullOrWhiteSpace(filter) ||
                topic.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                _children.Add(topic);
        }
    }

    // ── Single character (many matches) ──────────────────────────────────────

    [Benchmark(Description = "Filter – single char (\"e\", high match rate)")]
    public void Filter_SingleChar()
    {
        _children.Clear();
        const string filter = "e";
        foreach (var topic in _topics)
        {
            if (string.IsNullOrWhiteSpace(filter) ||
                topic.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                _children.Add(topic);
        }
    }

    // ── Exact name (1 result) ─────────────────────────────────────────────────

    [Benchmark(Description = "Filter – exact topic name (1 result)")]
    public void Filter_ExactMatch()
    {
        _children.Clear();
        var filter = _topics.Count > 0 ? _topics[TopicCount / 2].Name : "";
        foreach (var topic in _topics)
        {
            if (string.IsNullOrWhiteSpace(filter) ||
                topic.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                _children.Add(topic);
        }
    }
}
