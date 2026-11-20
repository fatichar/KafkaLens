using System.IO;
using System.Threading;
using Avalonia.Threading;
using KafkaLens.Shared.Models;
using Avalonia.Headless.XUnit;
using KafkaLens.Shared;
using KafkaLens.ViewModels.Services;
using KafkaLens.ViewModels.Telemetry;

namespace KafkaLens.ViewModels.Tests.Telemetry;

public class TelemetryWiringTests
{
    private readonly ISettingsService settingsService = Substitute.For<ISettingsService>();
    private readonly ITopicSettingsService topicSettingsService = Substitute.For<ITopicSettingsService>();
    private readonly RecordingTelemetry telemetry = new();

    private OpenedClusterViewModel CreateViewModel(IKafkaLensClient? client = null, ClusterViewModel? clusterVm = null)
    {
        settingsService.GetBrowserConfig().Returns(new BrowserConfig());
        topicSettingsService.GetSettings(Arg.Any<string>(), Arg.Any<string>()).Returns(new TopicSettings());
        var cluster = new KafkaCluster("c1", "Cluster", "localhost:9092");
        clusterVm ??= new ClusterViewModel(cluster, client ?? Substitute.For<IKafkaLensClient>(), telemetry: telemetry);
        return new OpenedClusterViewModel(settingsService, topicSettingsService, Substitute.For<IMessageSaver>(),
            new FormatterService(), clusterVm, "Cluster", telemetry: telemetry);
    }

    [AvaloniaFact]
    public async Task CopyCommands_TrackFeature()
    {
        var vm = CreateViewModel();

        await vm.CopyKeyCommand.ExecuteAsync(null);
        await vm.CopyAllCommand.ExecuteAsync(null);

        telemetry.Features.Should().Contain(new[] { TelemetrySchema.Features.CopyKey, TelemetrySchema.Features.CopyAll });
    }

    [AvaloniaFact]
    public void MessageFilters_AreTrackedOncePerKind_WithoutText()
    {
        var vm = CreateViewModel();

        vm.CurrentMessages.PositiveFilter = "o";
        vm.CurrentMessages.PositiveFilter = "or";
        vm.CurrentMessages.PositiveFilter = "order-id-123";
        vm.CurrentMessages.NegativeFilter = "x";

        telemetry.Features.Count(f => f == TelemetrySchema.Features.PositiveFilter).Should().Be(1);
        telemetry.Features.Should().Contain(TelemetrySchema.Features.NegativeFilter);
        telemetry.Events.SelectMany(e => e.Props.Values).OfType<string>().Should().NotContain(v => v.Contains("order-id"));
    }

    [AvaloniaFact]
    public void RestoredTabState_IsNotCountedAsFeatureUse()
    {
        var vm = CreateViewModel();

        vm.ApplyOpenedTabState(new OpenedTabState
        {
            ClusterId = "c1",
            PositiveFilter = "restored",
            MessagesSortColumn = "Offset",
            MessagesSortAscending = true
        });

        telemetry.Events.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void TopicFilter_IsTrackedOncePerTab()
    {
        var vm = CreateViewModel();

        vm.FilterText = "ord";
        vm.FilterText = "orders";

        telemetry.Features.Count(f => f == TelemetrySchema.Features.TopicFilter).Should().Be(1);
    }

    [AvaloniaFact]
    public async Task SharedAndCachedTopicLoads_RecordOneRequestAndAvailabilityPerTab()
    {
        var client = Substitute.For<IKafkaLensClient>();
        var ready = new TaskCompletionSource<IList<Topic>>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.GetTopicsAsync("c1").Returns(ready.Task);
        var cluster = new ClusterViewModel(new KafkaCluster("c1", "Cluster", "localhost:9092"), client, telemetry: telemetry);
        using var first = CreateViewModel(clusterVm: cluster);
        using var second = CreateViewModel(clusterVm: cluster);

        var firstLoad = first.LoadTopicsAsync();
        var secondLoad = second.LoadTopicsAsync();
        ready.SetResult(new List<Topic> { new("private-topic", new List<Partition>()) });
        await Task.WhenAll(firstLoad, secondLoad);
        using var third = CreateViewModel(clusterVm: cluster);
        await third.LoadTopicsAsync();
        await client.Received(1).GetTopicsAsync("c1");

        var timing = telemetry.Events.Should().ContainSingle(e => e.Name == TelemetrySchema.Events.ClusterConnected).Which;
        timing.Props["attempts"].Should().Be(1);
        timing.Props["result"].Should().Be("ok");
        timing.Props["duration_ms"].Should().BeOfType<long>();
        telemetry.Events.Count(e => e.Name == TelemetrySchema.Events.TopicsAvailable).Should().Be(3);
        telemetry.Events.SelectMany(e => e.Props.Values).Should().NotContain("private-topic");
    }

    [AvaloniaFact]
    public async Task FailedTopicRequest_ReportsFailureAndAvailabilityOnlyAfterRecovery()
    {
        var client = Substitute.For<IKafkaLensClient>();
        client.GetTopicsAsync("c1").Returns(
            Task.FromException<IList<Topic>>(new InvalidOperationException("internal details")),
            Task.FromResult<IList<Topic>>(new List<Topic>()));
        var cluster = new ClusterViewModel(new KafkaCluster("c1", "Cluster", "localhost:9092"), client, telemetry: telemetry);
        using var tab = CreateViewModel(clusterVm: cluster);

        await cluster.EnsureTopicsLoadedAsync(maxRetries: 0);
        telemetry.Events.Should().NotContain(e => e.Name == TelemetrySchema.Events.TopicsAvailable);
        await cluster.EnsureTopicsLoadedAsync(maxRetries: 0);

        var timings = telemetry.Events.Where(e => e.Name == TelemetrySchema.Events.ClusterConnected).ToArray();
        timings.Select(e => e.Props["result"]).Should().Equal("failed", "ok");
        timings.Select(e => e.Props["duration_ms"]).Should().OnlyContain(duration => duration is long);
        telemetry.Events.Should().ContainSingle(e => e.Name == TelemetrySchema.Events.TopicsAvailable)
            .Which.Props["source"].Should().Be("health_check");
    }

    [AvaloniaFact]
    public async Task ReplacedFetch_RecordsSupersededAndSuccessfulEmptyFetchOnceEach()
    {
        var client = Substitute.For<IKafkaLensClient>();
        client.GetTopicsAsync("c1").Returns(new List<Topic> { new("topic", new List<Partition>()) });
        var previous = new MessageStream();
        var current = new MessageStream();
        client.GetMessageStream("c1", "topic", Arg.Any<FetchOptions>(), Arg.Any<CancellationToken>())
            .Returns(previous, current);
        using var vm = CreateViewModel(client);
        await vm.LoadTopicsAsync();
        vm.IsCurrent = true;
        vm.SelectedNode = vm.Topics[0];
        vm.RefreshCommand.Execute(null);
        previous.HasMore = false;
        current.HasMore = false;
        Dispatcher.UIThread.RunJobs();

        var fetches = telemetry.Events.Where(e => e.Name == TelemetrySchema.Events.FetchCompleted).ToArray();
        fetches.Select(e => e.Props["result"]).Should().Equal("superseded", "ok");
        fetches.Select(e => e.Props["duration_ms"]).Should().OnlyContain(duration => duration is long);
        fetches[1].Props["received"].Should().Be("0");
    }

    [Fact]
    public void PreferencesSave_AppliesTelemetryToggle()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kl-prefs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var settings = new SettingsService(Path.Combine(dir, "settings.json"));
        var control = Substitute.For<ITelemetryControl, KafkaLens.Shared.Telemetry.ITelemetry>();
        control.IsAvailable.Returns(true);
        control.UserEnabled.Returns(true);
        control.RecentEvents.Returns(Array.Empty<string>());

        var vm = new PreferencesViewModel(settings, telemetry: (KafkaLens.Shared.Telemetry.ITelemetry)control);
        vm.IsTelemetryAvailable.Should().BeTrue();
        vm.SendUsageData.Should().BeTrue();

        vm.SendUsageData = false;
        vm.SaveCommand.Execute(null);

        control.Received(1).SetEnabled(false);
    }
}

public class SettingsSnapshotTests
{
    private static SettingsService NewSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kl-snap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new SettingsService(Path.Combine(dir, "settings.json"));
    }

    private static readonly SettingsSnapshot.Inventory Inventory = new(3, 2, 1, 0, 1, 1);

    [Fact]
    public void Build_UsesOnlyAllowListedKeys_AndFlagsDefaults()
    {
        var props = SettingsSnapshot.Build(NewSettings(), Inventory, "daily");

        props.Keys.Should().BeSubsetOf(TelemetrySchema.EventProperties[TelemetrySchema.Events.SettingsSnapshot]);
        props.Where(p => p.Key.EndsWith("_is_default")).Should().OnlyContain(p => (bool)p.Value! == true);
        props["clusters"].Should().Be("2-5");
    }

    [Fact]
    public void Build_DetectsChangedSettings()
    {
        var settings = NewSettings();
        var browser = settings.GetBrowserConfig();
        browser.DefaultFetchCount = 500;
        browser.SavedMessagesDirectory = "/home/someone/private";
        settings.SaveBrowserConfig(browser);

        var props = SettingsSnapshot.Build(settings, Inventory, "daily");

        props["default_fetch_count"].Should().Be(500);
        props["default_fetch_count_is_default"].Should().Be(false);
        props["saved_dir_is_default"].Should().Be(false);
        props.Values.OfType<string>().Should().NotContain(v => v.Contains("someone"));
    }

    [Fact]
    public void SendIfDue_SendsAtMostOncePerDay()
    {
        var settings = NewSettings();
        var telemetry = new RecordingTelemetry();
        var now = DateTimeOffset.UtcNow;

        SettingsSnapshot.SendIfDue(telemetry, settings, Inventory, now);
        SettingsSnapshot.SendIfDue(telemetry, settings, Inventory, now.AddHours(2));
        SettingsSnapshot.SendIfDue(telemetry, settings, Inventory, now.AddHours(25));

        telemetry.Events.Count(e => e.Name == TelemetrySchema.Events.SettingsSnapshot).Should().Be(2);
    }
}

public class PrivacyPageTests
{
    /// <summary>The public privacy page must document every event and property the app can send.</summary>
    [Fact]
    public void PrivacyPage_ListsEveryEventAndProperty()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KafkaLens.slnx"))) dir = dir.Parent;
        dir.Should().NotBeNull();
        var html = File.ReadAllText(Path.Combine(dir!.FullName, "docs", "privacy.html"));

        var missing = TelemetrySchema.EventProperties.Keys
            .Concat(TelemetrySchema.EventProperties.Values.SelectMany(v => v))
            .Concat(TelemetrySchema.CommonProperties)
            .Concat(typeof(TelemetrySchema.Features).GetFields().Select(f => (string)f.GetValue(null)!))
            .Distinct()
            .Where(name => !html.Contains($"<code>{name}</code>", StringComparison.Ordinal))
            .ToList();

        missing.Should().BeEmpty("docs/privacy.html must list every telemetry event, property and feature");
    }
}
