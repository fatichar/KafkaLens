using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using KafkaLens.Shared.Telemetry;
using KafkaLens.ViewModels.Telemetry;

namespace KafkaLens.ViewModels.Tests.Telemetry;

internal sealed class RecordingTelemetry : ITelemetry
{
    public bool IsEnabled { get; set; } = true;
    public List<(string Name, Dictionary<string, object?> Props)> Events { get; } = new();

    public void Track(string eventName, IReadOnlyDictionary<string, object?>? properties = null) =>
        Events.Add((eventName, properties?.ToDictionary(p => p.Key, p => p.Value) ?? new()));

    public IEnumerable<string> Features =>
        Events.Where(e => e.Name == TelemetrySchema.Events.FeatureUsed).Select(e => (string)e.Props["feature"]!);
}

internal sealed class RecordingSink : ITelemetrySink
{
    public ConcurrentQueue<TelemetryEvent> Sent { get; } = new();
    public bool Fail { get; set; }

    public Task<bool> SendAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
    {
        if (Fail) return Task.FromResult(false);
        Sent.Enqueue(telemetryEvent);
        return Task.FromResult(true);
    }
}

/// <summary>Blocks every send until cancelled; signals when a send has started.</summary>
internal sealed class BlockingSink : ITelemetrySink
{
    public TaskCompletionSource SendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool WasCancelled { get; private set; }

    public async Task<bool> SendAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
    {
        SendStarted.TrySetResult();
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            WasCancelled = true;
            throw;
        }
    }
}

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        return respond(request);
    }
}

/// <summary>Creates a real TelemetryService over a temp directory and real settings file.</summary>
internal sealed class TelemetryFixture : IAsyncDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "kl-telemetry-" + Guid.NewGuid().ToString("N"));
    public SettingsService Settings { get; }
    public RecordingSink Sink { get; } = new();
    public ITelemetrySink ActiveSink { get; }
    public TelemetrySpool Spool { get; }
    public ActivityTracker Activity { get; } = new();
    public TelemetryService Service { get; }

    public static readonly TelemetryEnvironment Env =
        new("1.2.3", "linux", "6.1.0", "x64", "10.0", "zip", "en-US");

    public TelemetryFixture(
        TelemetryOptions? options = null,
        Dictionary<string, string?>? env = null,
        Action<SettingsService>? seedSettings = null,
        Func<CancellationToken, Task<bool>>? remoteCheck = null,
        ITelemetrySink? sink = null)
    {
        ActiveSink = sink ?? Sink;
        Directory.CreateDirectory(Dir);
        Settings = new SettingsService(Path.Combine(Dir, "settings.json"));
        seedSettings?.Invoke(Settings);
        Spool = new TelemetrySpool(Path.Combine(Dir, "telemetry", "queue.jsonl"), 1_000_000, TimeSpan.FromDays(7));
        Service = new TelemetryService(
            options ?? new TelemetryOptions { WebsiteId = "site", FlushIntervalSeconds = 3600 },
            Settings, ActiveSink, Spool, Env, Activity,
            Path.Combine(Dir, "telemetry", "session.lock"),
            name => env != null && env.TryGetValue(name, out var v) ? v : null,
            remoteCheck);
    }

    public async ValueTask DisposeAsync()
    {
        await Service.DisposeAsync();
        try { Directory.Delete(Dir, true); } catch (IOException) { }
    }
}
