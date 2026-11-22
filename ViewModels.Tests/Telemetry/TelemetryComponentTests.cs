using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using KafkaLens.ViewModels.Telemetry;

namespace KafkaLens.ViewModels.Tests.Telemetry;

public class TelemetryBucketsTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(5, "2-5")]
    [InlineData(10, "6-10")]
    [InlineData(100, "51-100")]
    [InlineData(1000, "501-1k")]
    [InlineData(25000, "10k-50k")]
    [InlineData(1_000_000, "50k+")]
    public void Count(long value, string expected) => TelemetryBuckets.Count(value).Should().Be(expected);

    [Theory]
    [InlineData(-5, 0L)]
    [InlineData(999, 999L)]
    [InlineData(900_000, 600_000L)]
    public void Milliseconds(int ms, long expected) =>
        TelemetryBuckets.Milliseconds(TimeSpan.FromMilliseconds(ms)).Should().Be(expected);
}

public class ErrorReportTests
{
    [Fact]
    public void Build_ContainsTypesAndFrames_ButNeverTheMessage()
    {
        Exception ex = new InvalidOperationException();
        try { Throw(); }
        catch (Exception e) { ex = new AggregateException(e); }

        var report = ErrorReport.Build(ex, "unobserved_task");

        report["kind"].Should().Be("unobserved_task");
        report["exception_type"].Should().Be("System.IO.IOException");
        report["inner_type"].Should().Be("System.UnauthorizedAccessException");
        ((string)report["frames"]!).Should().Contain(nameof(ErrorReportTests) + "." + nameof(Throw));
        ((string)report["fingerprint"]!).Should().HaveLength(12);
        string.Join(" ", report.Values).Should().NotContain("password").And.NotContain("/secret/path");
    }

    [Fact]
    public void Fingerprint_IsStableForSameFailure()
    {
        static Exception Capture()
        {
            try { Throw(); return null!; }
            catch (Exception e) { return e; }
        }

        ErrorReport.Build(Capture(), "x")["fingerprint"].Should().Be(ErrorReport.Build(Capture(), "x")["fingerprint"]);
    }

    private static void Throw() =>
        throw new IOException("password=hunter2 /secret/path", new UnauthorizedAccessException("/secret/path"));
}

public class TelemetrySpoolTests
{
    private static TelemetryEvent Event(DateTimeOffset ts, string name = "heartbeat") =>
        new(name, ts, "id", new Dictionary<string, object?> { ["active_s"] = 30L, ["os"] = "linux", ["ok"] = true });

    [Fact]
    public void AppendAndDrain_RoundTripsPrimitiveValues()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonl");
        var spool = new TelemetrySpool(path, 1_000_000, TimeSpan.FromDays(7));
        var now = DateTimeOffset.UtcNow;

        spool.Append(new[] { Event(now) });
        var drained = spool.Drain();

        drained.Should().ContainSingle();
        drained[0].Properties["active_s"].Should().Be(30L);
        drained[0].Properties["ok"].Should().Be(true);
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void Drain_DropsExpiredEvents()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonl");
        var now = DateTimeOffset.UtcNow;
        var spool = new TelemetrySpool(path, 1_000_000, TimeSpan.FromDays(7), () => now);

        spool.Append(new[] { Event(now.AddDays(-8), "old"), Event(now, "new") });

        spool.Drain().Select(e => e.Name).Should().Equal("new");
    }

    [Fact]
    public void Append_EnforcesSizeLimit_KeepingNewest()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonl");
        var spool = new TelemetrySpool(path, 2_000, TimeSpan.FromDays(7));
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 100; i++) spool.Append(new[] { Event(now, "e" + i) });

        new FileInfo(path).Length.Should().BeLessThanOrEqualTo(2_000);
        spool.Drain().Last().Name.Should().Be("e99");
    }
}

public class UmamiTelemetrySinkTests
{
    private static readonly TelemetryOptions Options = new() { WebsiteId = "site-1", Endpoint = "https://t.example/api/send" };

    private static TelemetryEvent Event() => new("fetch_completed", DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), "install-1",
        new Dictionary<string, object?> { ["locale"] = "en-US", ["result"] = "ok" });

    [Fact]
    public async Task Send_PostsUmamiPayload_WithDistinctIdAndBrowserLikeUserAgent()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sink = new UmamiTelemetrySink(new HttpClient(handler), Options, TelemetryFixture.Env);

        (await sink.SendAsync(Event(), CancellationToken.None)).Should().BeTrue();

        var (request, body) = handler.Requests.Single();
        request.RequestUri!.ToString().Should().Be("https://t.example/api/send");
        request.Headers.UserAgent.ToString().Should().StartWith("Mozilla/5.0 (X11; Linux x86_64)").And.Contain("KafkaLens/1.2.3");
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("type").GetString().Should().Be("event");
        var payload = doc.RootElement.GetProperty("payload");
        payload.GetProperty("website").GetString().Should().Be("site-1");
        payload.GetProperty("name").GetString().Should().Be("fetch_completed");
        payload.GetProperty("id").GetString().Should().Be("install-1");
        payload.GetProperty("timestamp").GetInt64().Should().Be(1_700_000_000);
        payload.GetProperty("data").GetProperty("result").GetString().Should().Be("ok");
    }

    [Fact]
    public async Task Send_ReturnsFalseOnThrottle_AndBacksOff()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var sink = new UmamiTelemetrySink(new HttpClient(handler), Options, TelemetryFixture.Env);

        (await sink.SendAsync(Event(), CancellationToken.None)).Should().BeFalse();
        (await sink.SendAsync(Event(), CancellationToken.None)).Should().BeFalse();
        handler.Requests.Should().HaveCount(1, "the second call is inside the back-off window");
    }

    [Fact]
    public async Task Send_TreatsClientErrorAsDone_SoBadEventsAreNotRetriedForever()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var sink = new UmamiTelemetrySink(new HttpClient(handler), Options, TelemetryFixture.Env);

        (await sink.SendAsync(Event(), CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task Send_ReturnsFalseOnNetworkError()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("offline"));
        var sink = new UmamiTelemetrySink(new HttpClient(handler), Options, TelemetryFixture.Env);

        (await sink.SendAsync(Event(), CancellationToken.None)).Should().BeFalse();
    }
}

public class ActivityTrackerTests
{
    [Fact]
    public void IsActive_RequiresFocusAndRecentInput()
    {
        long now = 0;
        var tracker = new ActivityTracker(() => now);

        tracker.IsActive(TimeSpan.FromSeconds(60)).Should().BeFalse("not focused");
        tracker.SetFocused(true);
        tracker.IsActive(TimeSpan.FromSeconds(60)).Should().BeTrue();

        now = 61_000;
        tracker.IsActive(TimeSpan.FromSeconds(60)).Should().BeFalse("idle");
        tracker.RecordInput();
        tracker.IsActive(TimeSpan.FromSeconds(60)).Should().BeTrue();

        tracker.SetFocused(false);
        tracker.IsActive(TimeSpan.FromSeconds(60)).Should().BeFalse();
    }
}

public class TelemetryExtensionsTests
{
    [Theory]
    [InlineData(typeof(TimeoutException), "timeout")]
    [InlineData(typeof(OperationCanceledException), "cancelled")]
    [InlineData(typeof(UnauthorizedAccessException), "auth")]
    [InlineData(typeof(HttpRequestException), "network")]
    [InlineData(typeof(InvalidOperationException), "other")]
    public void ErrorCategory_MapsExceptionTypes(Type type, string expected) =>
        TelemetryExtensions.ErrorCategory((Exception)Activator.CreateInstance(type)!).Should().Be(expected);

    [Fact]
    public void FormatterName_HidesPluginFormatterNames()
    {
        var builtIn = new HashSet<string> { "Json", "Text" };
        TelemetryExtensions.FormatterName("Json", builtIn).Should().Be("Json");
        TelemetryExtensions.FormatterName("AcmeCorpInternalProto", builtIn).Should().Be("plugin");
    }
}
