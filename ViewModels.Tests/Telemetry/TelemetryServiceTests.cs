using System.IO;
using System.Threading;
using KafkaLens.ViewModels.Telemetry;

namespace KafkaLens.ViewModels.Tests.Telemetry;

public class TelemetryServiceTests
{
    private static async Task FlushAsync(TelemetryService service) =>
        await service.FlushAsync(includeSpool: true, CancellationToken.None);

    [Fact]
    public async Task NotConfigured_IsUnavailable_AndSendsNothing()
    {
        await using var f = new TelemetryFixture(new TelemetryOptions { WebsiteId = "" });
        f.Service.Start(runBackgroundLoops: false);

        f.Service.IsAvailable.Should().BeFalse();
        f.Service.IsEnabled.Should().BeFalse();
        f.Service.NeedsNotice.Should().BeFalse();
        f.Service.Track(TelemetrySchema.Events.FeatureUsed, new Dictionary<string, object?> { ["feature"] = "x" });
        await FlushAsync(f.Service);

        f.Sink.Sent.Should().BeEmpty();
        f.Service.InstallId.Should().BeEmpty("no install id is generated when telemetry is unavailable");
    }

    [Theory]
    [InlineData("DO_NOT_TRACK", "1")]
    [InlineData("DO_NOT_TRACK", "true")]
    [InlineData("KAFKALENS_TELEMETRY", "0")]
    [InlineData("KAFKALENS_TELEMETRY", "off")]
    public async Task EnvironmentVariable_ForcesTelemetryOff(string name, string value)
    {
        await using var f = new TelemetryFixture(env: new() { [name] = value });

        f.Service.IsForcedOff.Should().BeTrue();
        f.Service.IsEnabled.Should().BeFalse();
        f.Service.NeedsNotice.Should().BeFalse();
    }

    [Fact]
    public async Task FirstRun_GeneratesInstallId_AndNeedsNotice()
    {
        await using var f = new TelemetryFixture();
        f.Service.Start(runBackgroundLoops: false);

        Guid.TryParse(f.Service.InstallId, out _).Should().BeTrue();
        f.Settings.GetValue(TelemetryService.InstallIdKey).Should().Be(f.Service.InstallId);
        f.Service.NeedsNotice.Should().BeTrue();
    }

    [Fact]
    public async Task EventsQueuedBeforeNotice_AreSentAfterAcknowledge()
    {
        await using var f = new TelemetryFixture();
        f.Service.Start(runBackgroundLoops: false);
        f.Service.Feature(TelemetrySchema.Features.CopyKey);

        f.Service.AcknowledgeNotice(enabled: true);
        await FlushAsync(f.Service);

        f.Sink.Sent.Select(e => e.Name).Should().Contain(new[]
        {
            TelemetrySchema.Events.AppFirstRun, TelemetrySchema.Events.FeatureUsed
        });
        f.Service.NeedsNotice.Should().BeFalse();
        f.Service.RecentEvents.Should().NotBeEmpty();
    }

    [Fact]
    public async Task DecliningNotice_DropsQueuedEvents_AndDisables()
    {
        await using var f = new TelemetryFixture();
        f.Service.Start(runBackgroundLoops: false);
        f.Service.Feature(TelemetrySchema.Features.CopyKey);

        f.Service.AcknowledgeNotice(enabled: false);
        f.Service.Feature(TelemetrySchema.Features.CopyValue);
        await FlushAsync(f.Service);

        f.Sink.Sent.Should().BeEmpty();
        f.Service.IsEnabled.Should().BeFalse();
        f.Settings.GetValue(TelemetryService.EnabledKey).Should().Be("false");
        File.Exists(f.Spool.FilePath).Should().BeFalse();
    }

    [Fact]
    public async Task Track_AddsCommonProps_AndDropsPropertiesOutsideAllowList()
    {
        await using var f = new TelemetryFixture();
        f.Service.Start(runBackgroundLoops: false);
        f.Service.AcknowledgeNotice(true);

        f.Service.Track(TelemetrySchema.Events.FeatureUsed, new Dictionary<string, object?>
        {
            ["feature"] = "copy_key",
            ["topic"] = "orders-prod",          // not allowed
            ["object"] = new { Secret = "x" },   // not allowed
        });
        f.Service.Track("not_in_schema");
        await FlushAsync(f.Service);

        var e = f.Sink.Sent.Single(x => x.Name == TelemetrySchema.Events.FeatureUsed);
        e.Properties.Should().ContainKey("feature");
        e.Properties.Should().NotContainKeys("topic", "object");
        e.Properties["app_version"].Should().Be("1.2.3");
        e.Properties["os"].Should().Be("linux");
        e.Properties["schema"].Should().Be(TelemetrySchema.Version);
        e.InstallId.Should().Be(f.Service.InstallId);
        f.Sink.Sent.Should().NotContain(x => x.Name == "not_in_schema");
    }

    [Fact]
    public async Task Track_TruncatesLongStrings()
    {
        await using var f = new TelemetryFixture();
        f.Service.Start(runBackgroundLoops: false);
        f.Service.AcknowledgeNotice(true);

        f.Service.Track(TelemetrySchema.Events.FeatureUsed, new Dictionary<string, object?> { ["feature"] = new string('a', 500) });
        await FlushAsync(f.Service);

        ((string)f.Sink.Sent.Single(x => x.Name == TelemetrySchema.Events.FeatureUsed).Properties["feature"]!)
            .Length.Should().Be(200);
    }

    [Fact]
    public async Task SinkFailure_SpoolsEvents_AndReplaysThemLater()
    {
        await using var f = new TelemetryFixture();
        f.Service.Start(runBackgroundLoops: false);
        f.Service.AcknowledgeNotice(true);
        f.Sink.Fail = true;
        f.Service.Feature(TelemetrySchema.Features.CopyKey);
        await FlushAsync(f.Service);

        f.Sink.Sent.Should().BeEmpty();
        File.Exists(f.Spool.FilePath).Should().BeTrue();

        f.Sink.Fail = false;
        await FlushAsync(f.Service);

        f.Sink.Sent.Select(e => e.Name).Should().Contain(TelemetrySchema.Events.FeatureUsed);
        File.Exists(f.Spool.FilePath).Should().BeFalse();
    }

    [Fact]
    public async Task UserDisable_ClearsSpool_AndStopsTracking()
    {
        await using var f = new TelemetryFixture();
        f.Service.Start(runBackgroundLoops: false);
        f.Service.AcknowledgeNotice(true);
        f.Sink.Fail = true;
        f.Service.Feature(TelemetrySchema.Features.CopyKey);
        await FlushAsync(f.Service);

        f.Service.SetEnabled(false);
        f.Sink.Fail = false;
        f.Service.Feature(TelemetrySchema.Features.CopyValue);
        await FlushAsync(f.Service);

        f.Sink.Sent.Should().BeEmpty();
        File.Exists(f.Spool.FilePath).Should().BeFalse();
    }

    [Fact]
    public async Task TerminatingError_IsWrittenToSpoolSynchronously_WithoutMessage()
    {
        await using var f = new TelemetryFixture();
        f.Service.Start(runBackgroundLoops: false);
        f.Service.AcknowledgeNotice(true);

        Exception ex;
        try { throw new InvalidOperationException("secret topic orders-prod at /home/alice"); }
        catch (Exception e) { ex = e; }
        f.Service.ReportError(ex, "unhandled", terminating: true);

        var spooled = File.ReadAllText(f.Spool.FilePath);
        spooled.Should().Contain("app_error").And.Contain("System.InvalidOperationException");
        spooled.Should().NotContain("secret").And.NotContain("alice");
    }

    [Fact]
    public async Task RemoteKillSwitch_DisablesTelemetry()
    {
        await using var f = new TelemetryFixture(
            new TelemetryOptions { WebsiteId = "site", FlushIntervalSeconds = 3600 },
            remoteCheck: _ => Task.FromResult(false));
        f.Settings.SetValue(TelemetryService.NoticeVersionKey, TelemetryService.CurrentNoticeVersion.ToString());
        f.Service.Start();

        await WaitUntil(() => !f.Service.IsEnabled);
        f.Service.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task OptedOutUser_NeverContactsRemoteConfig_UntilReEnabled()
    {
        var calls = 0;
        await using var f = new TelemetryFixture(
            new TelemetryOptions { WebsiteId = "site", FlushIntervalSeconds = 1 },
            remoteCheck: _ => { Interlocked.Increment(ref calls); return Task.FromResult(true); });
        f.Service.Start();

        f.Service.AcknowledgeNotice(enabled: false);
        await Task.Delay(1500);
        Volatile.Read(ref calls).Should().Be(0);

        f.Service.SetEnabled(true);
        await WaitUntil(() => Volatile.Read(ref calls) > 0);
        Volatile.Read(ref calls).Should().Be(1);
    }

    [Fact]
    public async Task DisablingDuringFlush_CancelsSend_AndDoesNotRecreateSpool()
    {
        var sink = new BlockingSink();
        await using var f = new TelemetryFixture(sink: sink);
        f.Service.Start(runBackgroundLoops: false);
        f.Service.AcknowledgeNotice(true);
        f.Service.Feature(TelemetrySchema.Features.CopyKey);

        var flush = f.Service.FlushAsync(includeSpool: true, CancellationToken.None);
        await sink.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Service.SetEnabled(false);
        await flush.WaitAsync(TimeSpan.FromSeconds(5));

        sink.WasCancelled.Should().BeTrue();
        File.Exists(f.Spool.FilePath).Should().BeFalse();
    }

    [Fact]
    public async Task PreviousSessionWithoutCleanExit_IsReportedOnStartup()
    {
        await using var f = new TelemetryFixture();
        Directory.CreateDirectory(Path.Combine(f.Dir, "telemetry"));
        File.WriteAllText(Path.Combine(f.Dir, "telemetry", "session.lock"), "previous");
        f.Service.Start(runBackgroundLoops: false);
        f.Service.AcknowledgeNotice(true);
        f.Service.ReportStartup(TimeSpan.FromMilliseconds(800), pluginsLoaded: 2);
        await FlushAsync(f.Service);

        var started = f.Sink.Sent.Single(e => e.Name == TelemetrySchema.Events.AppStarted);
        started.Properties["previous_unclean_exit"].Should().Be(true);
        started.Properties["duration_ms"].Should().Be(800L);
        started.Properties["schema"].Should().Be(2);
        var firstRun = f.Sink.Sent.Single(e => e.Name == TelemetrySchema.Events.AppFirstRun);
        started.Properties["session_id"].Should().Be(firstRun.Properties["session_id"]);
        ((long)started.Properties["seq"]!).Should().BeGreaterThan((long)firstRun.Properties["seq"]!);
        ((long)started.Properties["launch_elapsed_ms"]!).Should().BeGreaterThanOrEqualTo(
            (long)firstRun.Properties["launch_elapsed_ms"]!);
        started.Properties["plugins_loaded"].Should().Be(2);
    }

    [Fact]
    public async Task Shutdown_SendsExitEvent_AndRemovesSessionMarker()
    {
        var f = new TelemetryFixture();
        f.Service.Start(runBackgroundLoops: false);
        f.Service.AcknowledgeNotice(true);
        var marker = Path.Combine(f.Dir, "telemetry", "session.lock");
        File.Exists(marker).Should().BeTrue();

        await f.Service.ShutdownAsync(TimeSpan.FromSeconds(2));

        f.Sink.Sent.Select(e => e.Name).Should().Contain(TelemetrySchema.Events.AppExited);
        File.Exists(marker).Should().BeFalse();
        f.Service.IsEnabled.Should().BeFalse();
        await f.DisposeAsync();
    }

    [Fact]
    public async Task ActiveTime_IsReportedViaHeartbeats()
    {
        var options = new TelemetryOptions
        {
            WebsiteId = "site", FlushIntervalSeconds = 3600,
            ActivitySampleSeconds = 1, HeartbeatIntervalSeconds = 1, IdleThresholdSeconds = 60
        };
        await using var f = new TelemetryFixture(options);
        f.Service.Start();
        f.Service.AcknowledgeNotice(true);
        f.Activity.SetFocused(true);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && !f.Sink.Sent.Any(e => e.Name == TelemetrySchema.Events.Heartbeat))
        {
            await Task.Delay(100);
            await FlushAsync(f.Service);
        }

        f.Sink.Sent.Where(e => e.Name == TelemetrySchema.Events.Heartbeat)
            .Sum(e => Convert.ToInt64(e.Properties["active_s"])).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task OptedOutTime_IsNotCounted_AfterReEnabling()
    {
        var options = new TelemetryOptions
        {
            WebsiteId = "site", FlushIntervalSeconds = 3600,
            ActivitySampleSeconds = 1, HeartbeatIntervalSeconds = 3600, IdleThresholdSeconds = 60
        };
        var f = new TelemetryFixture(options);
        f.Service.Start();
        f.Service.AcknowledgeNotice(false);
        f.Activity.SetFocused(true);

        await Task.Delay(2500); // active but opted out
        f.Service.SetEnabled(true);
        await f.Service.ShutdownAsync(TimeSpan.FromSeconds(2));

        var exited = f.Sink.Sent.Single(e => e.Name == TelemetrySchema.Events.AppExited);
        // At most one sample can land between re-enabling and shutdown.
        Convert.ToInt64(exited.Properties["session_active_s"]).Should().BeLessThanOrEqualTo(1);
        await f.DisposeAsync();
    }

    [Fact]
    public async Task ProcessExitWithoutShutdown_IsClean_AndSpoolsQueuedEvents()
    {
        await using var f = new TelemetryFixture();
        f.Service.Start(runBackgroundLoops: false);
        f.Service.AcknowledgeNotice(true);
        f.Service.Track(TelemetrySchema.Events.UpdateStarted, new Dictionary<string, object?> { ["to_version"] = "1.0.1" });

        f.Service.HandleProcessExit(); // e.g. Environment.Exit after launching the updater

        File.Exists(Path.Combine(f.Dir, "telemetry", "session.lock")).Should().BeFalse();
        File.ReadAllText(f.Spool.FilePath).Should().Contain(TelemetrySchema.Events.UpdateStarted);
    }

    [Fact]
    public async Task ProcessExitAfterTerminatingError_KeepsSessionMarker()
    {
        await using var f = new TelemetryFixture();
        f.Service.Start(runBackgroundLoops: false);
        f.Service.AcknowledgeNotice(true);

        f.Service.ReportError(new InvalidOperationException(), "unhandled", terminating: true);
        f.Service.HandleProcessExit();

        File.Exists(Path.Combine(f.Dir, "telemetry", "session.lock")).Should().BeTrue();
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(50);
    }
}
