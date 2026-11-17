using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using KafkaLens.Shared.Telemetry;
using Serilog;

namespace KafkaLens.ViewModels.Telemetry;

/// <summary>User-facing telemetry controls (Preferences / first-run notice).</summary>
public interface ITelemetryControl
{
    /// <summary>False when telemetry is compiled out, not configured, or forced off by env var.</summary>
    bool IsAvailable { get; }
    bool IsForcedOff { get; }
    bool UserEnabled { get; }
    bool NeedsNotice { get; }
    string InstallId { get; }
    string PrivacyUrl { get; }
    IReadOnlyList<string> RecentEvents { get; }
    void SetEnabled(bool enabled);
    void AcknowledgeNotice(bool enabled);
    void ResetInstallId();
}

/// <summary>
/// Non-blocking telemetry pipeline: Track → bounded channel → background sender → sink.
/// Unsent events are spooled to disk (bounded). Nothing is sent before the first-run notice
/// is acknowledged, and nothing at all when disabled. See docs/analytics-plan.md.
/// </summary>
public sealed class TelemetryService : ITelemetry, ITelemetryControl, IAsyncDisposable
{
    public const int CurrentNoticeVersion = 1;
    internal const string EnabledKey = "TelemetryEnabled";
    internal const string InstallIdKey = "TelemetryInstallId";
    internal const string NoticeVersionKey = "TelemetryNoticeVersion";
    private const int MaxStringLength = 200;
    private const int RecentEventsCapacity = 100;

    private readonly TelemetryOptions options;
    private readonly ISettingsService settings;
    private readonly ITelemetrySink sink;
    private readonly TelemetrySpool spool;
    private readonly TelemetryEnvironment environment;
    private readonly ActivityTracker activity;
    private readonly string sessionMarkerPath;
    private readonly Func<CancellationToken, Task<bool>>? remoteEnabledCheck;
    private readonly Channel<TelemetryEvent> channel;
    private readonly TaskCompletionSource consentGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource loopCts = new();
    private readonly SemaphoreSlim flushLock = new(1, 1);
    private CancellationTokenSource disableCts = new();
    private bool remoteConfigChecked;
    private readonly ConcurrentQueue<string> recentEvents = new();
    private readonly Stopwatch sessionClock = Stopwatch.StartNew();
    private readonly string sessionId = Guid.NewGuid().ToString("N");

    private volatile bool userEnabled;
    private volatile bool remoteDisabled;
    private volatile string installId;
    private bool previousUncleanExit;
    private bool started;
    private bool shutDown;
    private long activeSecondsSinceHeartbeat;
    private long activeSecondsTotal;
    private Task? senderLoop;
    private Task? activityLoop;

    public TelemetryService(
        TelemetryOptions options,
        ISettingsService settings,
        ITelemetrySink sink,
        TelemetrySpool spool,
        TelemetryEnvironment environment,
        ActivityTracker activity,
        string sessionMarkerPath,
        Func<string, string?>? getEnvironmentVariable = null,
        Func<CancellationToken, Task<bool>>? remoteEnabledCheck = null)
    {
        this.options = options;
        this.settings = settings;
        this.sink = sink;
        this.spool = spool;
        this.environment = environment;
        this.activity = activity;
        this.sessionMarkerPath = sessionMarkerPath;
        this.remoteEnabledCheck = remoteEnabledCheck;

        IsForcedOff = IsDisabledByEnvironment(getEnvironmentVariable ?? Environment.GetEnvironmentVariable);
        userEnabled = !string.Equals(settings.GetValue(EnabledKey), "false", StringComparison.OrdinalIgnoreCase);
        installId = settings.GetValue(InstallIdKey) ?? "";

        channel = Channel.CreateBounded<TelemetryEvent>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    // ── ITelemetry ──────────────────────────────────────────────────────────

    public bool IsEnabled => IsAvailable && userEnabled && !remoteDisabled && !shutDown;

    public void Track(string eventName, IReadOnlyDictionary<string, object?>? properties = null)
    {
        try
        {
            if (!IsEnabled) return;
            var telemetryEvent = CreateEvent(eventName, properties);
            if (telemetryEvent != null) channel.Writer.TryWrite(telemetryEvent);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Telemetry Track failed for {Event}", eventName);
        }
    }

    // ── ITelemetryControl ───────────────────────────────────────────────────

    public bool IsAvailable => options.IsConfigured && !IsForcedOff;
    public bool IsForcedOff { get; }
    public bool UserEnabled => userEnabled;
    public bool NeedsNotice => IsAvailable && GetNoticeVersion() < CurrentNoticeVersion;
    public string InstallId => installId;
    public string PrivacyUrl => options.PrivacyUrl;
    public IReadOnlyList<string> RecentEvents => recentEvents.ToArray();

    public void SetEnabled(bool enabled)
    {
        userEnabled = enabled;
        settings.SetValue(EnabledKey, enabled ? "true" : "false");
        if (!enabled)
        {
            // Abort any in-flight send so nothing more leaves the machine after opt-out.
            Interlocked.Exchange(ref disableCts, new CancellationTokenSource()).Cancel();
            Interlocked.Exchange(ref activeSecondsSinceHeartbeat, 0);
            Interlocked.Exchange(ref activeSecondsTotal, 0);
            while (channel.Reader.TryRead(out _)) { }
            spool.Clear();
        }
        Log.Information("Telemetry {State} by user", enabled ? "enabled" : "disabled");
    }

    public void AcknowledgeNotice(bool enabled)
    {
        SetEnabled(enabled);
        settings.SetValue(NoticeVersionKey, CurrentNoticeVersion.ToString());
        consentGate.TrySetResult();
    }

    public void ResetInstallId()
    {
        installId = Guid.NewGuid().ToString();
        settings.SetValue(InstallIdKey, installId);
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────

    /// <summary>Starts background loops and records first-run. Idempotent.</summary>
    public void Start() => Start(runBackgroundLoops: true);

    /// <summary>Tests pass false to drive <see cref="FlushAsync"/> deterministically.</summary>
    internal void Start(bool runBackgroundLoops)
    {
        if (started || !IsAvailable) return;
        started = true;

        var firstRun = string.IsNullOrEmpty(installId);
        if (firstRun) ResetInstallId();

        try
        {
            previousUncleanExit = File.Exists(sessionMarkerPath);
            Directory.CreateDirectory(Path.GetDirectoryName(sessionMarkerPath)!);
            File.WriteAllText(sessionMarkerPath, sessionId);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not write telemetry session marker");
        }

        if (firstRun) Track(TelemetrySchema.Events.AppFirstRun);
        if (!NeedsNotice) consentGate.TrySetResult();

        if (!runBackgroundLoops) return;
        senderLoop = Task.Run(() => RunSenderAsync(loopCts.Token));
        activityLoop = Task.Run(() => RunActivityAsync(loopCts.Token));
    }

    public void ReportStartup(TimeSpan startupDuration, int pluginsLoaded)
    {
        Track(TelemetrySchema.Events.AppStarted, new Dictionary<string, object?>
        {
            ["startup_ms"] = TelemetryBuckets.Duration(startupDuration),
            ["previous_unclean_exit"] = previousUncleanExit,
            ["plugins_loaded"] = pluginsLoaded,
        });
    }

    /// <summary>
    /// Records an unexpected failure. When <paramref name="terminating"/> the event is written to the
    /// spool synchronously (the process is about to die) and sent on next launch.
    /// </summary>
    public void ReportError(Exception exception, string kind, bool terminating)
    {
        try
        {
            if (!IsEnabled) return;
            var telemetryEvent = CreateEvent(TelemetrySchema.Events.AppError, ErrorReport.Build(exception, kind));
            if (telemetryEvent == null) return;
            if (terminating) spool.Append(new[] { telemetryEvent });
            else channel.Writer.TryWrite(telemetryEvent);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Telemetry ReportError failed");
        }
    }

    /// <summary>Emits the final events and flushes, bounded by <paramref name="timeout"/>.</summary>
    /// <summary>Synchronously moves queued events to the spool (process is exiting abruptly).</summary>
    public void PersistPending()
    {
        try
        {
            if (shutDown || !consentGate.Task.IsCompleted || !IsEnabled) return;
            var pending = new List<TelemetryEvent>();
            while (channel.Reader.TryRead(out var e)) pending.Add(e);
            spool.Append(pending);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Telemetry PersistPending failed");
        }
    }

    public async Task ShutdownAsync(TimeSpan timeout)
    {
        if (shutDown) return;
        if (started)
        {
            var remaining = Interlocked.Exchange(ref activeSecondsSinceHeartbeat, 0);
            if (remaining > 0)
                Track(TelemetrySchema.Events.Heartbeat, new Dictionary<string, object?> { ["active_s"] = remaining });
            Track(TelemetrySchema.Events.AppExited, new Dictionary<string, object?>
            {
                ["session_active_s"] = Interlocked.Read(ref activeSecondsTotal),
                ["session_wall_s"] = TelemetryBuckets.Seconds(sessionClock.Elapsed),
            });
        }
        shutDown = true;
        loopCts.Cancel();

        try
        {
            if (senderLoop != null) await senderLoop.ConfigureAwait(false);
            if (activityLoop != null) await activityLoop.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Telemetry loop ended with error");
        }

        if (consentGate.Task.IsCompleted && userEnabled && IsAvailable)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            await FlushAsync(includeSpool: false, timeoutCts.Token).ConfigureAwait(false);
        }

        try
        {
            if (File.Exists(sessionMarkerPath)) File.Delete(sessionMarkerPath);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not delete telemetry session marker");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        loopCts.Dispose();
        flushLock.Dispose();
        disableCts.Dispose();
    }

    // ── Internals ───────────────────────────────────────────────────────────

    private TelemetryEvent? CreateEvent(string eventName, IReadOnlyDictionary<string, object?>? properties)
    {
        if (!TelemetrySchema.EventProperties.TryGetValue(eventName, out var allowed))
        {
            Log.Debug("Telemetry event {Event} is not in the schema; dropped", eventName);
            return null;
        }

        var props = new Dictionary<string, object?>
        {
            ["app_version"] = environment.AppVersion,
            ["os"] = environment.Os,
            ["os_version"] = environment.OsVersion,
            ["arch"] = environment.Arch,
            ["runtime"] = environment.Runtime,
            ["install_kind"] = environment.InstallKind,
            ["locale"] = environment.Locale,
            ["session_id"] = sessionId,
            ["schema"] = TelemetrySchema.Version,
        };

        if (properties != null)
        {
            foreach (var (key, value) in properties)
            {
                if (!allowed.Contains(key))
                {
                    Log.Debug("Telemetry property {Property} not allowed for {Event}; dropped", key, eventName);
                    continue;
                }
                var sanitized = Sanitize(value);
                if (sanitized != null) props[key] = sanitized;
            }
        }

        return new TelemetryEvent(eventName, DateTimeOffset.UtcNow, installId, props);
    }

    private static object? Sanitize(object? value) => value switch
    {
        null => null,
        string s => s.Length > MaxStringLength ? s[..MaxStringLength] : s,
        bool or int or long or double => value,
        Enum e => e.ToString().ToLowerInvariant(),
        _ => null // anything else (objects, collections) is never sent
    };

    private int GetNoticeVersion() =>
        int.TryParse(settings.GetValue(NoticeVersionKey), out var v) ? v : 0;

    private static bool IsDisabledByEnvironment(Func<string, string?> getEnv)
    {
        static bool IsTruthy(string? v) =>
            v != null && (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                          v.Equals("yes", StringComparison.OrdinalIgnoreCase));
        static bool IsFalsy(string? v) =>
            v != null && (v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                          v.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                          v.Equals("no", StringComparison.OrdinalIgnoreCase));

        return IsTruthy(getEnv("DO_NOT_TRACK")) || IsFalsy(getEnv("KAFKALENS_TELEMETRY"));
    }

    private async Task RunSenderAsync(CancellationToken ct)
    {
        try
        {
            await consentGate.Task.WaitAsync(ct).ConfigureAwait(false);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.FlushIntervalSeconds)));
            do
            {
                // No network contact at all (not even the kill-switch fetch) while the user has opted out.
                if (!userEnabled) continue;
                await CheckRemoteConfigOnceAsync(ct).ConfigureAwait(false);
                await FlushAsync(includeSpool: true, ct).ConfigureAwait(false);
            } while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Telemetry sender loop failed");
        }
    }

    private async Task CheckRemoteConfigOnceAsync(CancellationToken ct)
    {
        if (remoteConfigChecked || remoteEnabledCheck == null) return;
        remoteConfigChecked = true;
        try
        {
            remoteDisabled = !await remoteEnabledCheck(ct).ConfigureAwait(false);
            if (remoteDisabled) Log.Information("Telemetry disabled by remote config");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Debug(ex, "Telemetry remote config check failed");
        }
    }

    private bool CanSend => userEnabled && IsAvailable && !remoteDisabled;

    /// <summary>Sends spooled + queued events; anything not sent is spooled.</summary>
    internal async Task FlushAsync(bool includeSpool, CancellationToken ct)
    {
        try
        {
            await flushLock.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await FlushCoreAsync(includeSpool, ct).ConfigureAwait(false);
        }
        finally
        {
            flushLock.Release();
        }
    }

    private async Task FlushCoreAsync(bool includeSpool, CancellationToken ct)
    {
        var pending = new List<TelemetryEvent>();
        if (includeSpool) pending.AddRange(spool.Drain());
        while (channel.Reader.TryRead(out var e)) pending.Add(e);
        if (pending.Count == 0) return;

        if (!CanSend)
        {
            spool.Clear();
            return;
        }

        var sent = 0;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, Volatile.Read(ref disableCts).Token);
        try
        {
            foreach (var e in pending)
            {
                if (!CanSend) break;
                if (!await sink.SendAsync(e, linked.Token).ConfigureAwait(false)) break;
                RecordSent(e);
                sent++;
            }
        }
        catch (OperationCanceledException)
        {
            // timed out / shutting down — keep the rest for next launch
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Telemetry flush failed");
        }

        // Never re-create the spool after an opt-out that happened mid-flush.
        if (sent < pending.Count && CanSend) spool.Append(pending.Skip(sent));
    }

    private void RecordSent(TelemetryEvent e)
    {
        recentEvents.Enqueue(e.ToJson());
        while (recentEvents.Count > RecentEventsCapacity && recentEvents.TryDequeue(out _)) { }
    }

    private async Task RunActivityAsync(CancellationToken ct)
    {
        var sample = TimeSpan.FromSeconds(Math.Max(1, options.ActivitySampleSeconds));
        var idle = TimeSpan.FromSeconds(Math.Max(1, options.IdleThresholdSeconds));
        var samplesPerHeartbeat = Math.Max(1, options.HeartbeatIntervalSeconds / (int)sample.TotalSeconds);
        var samples = 0;

        try
        {
            using var timer = new PeriodicTimer(sample);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                // Opted-out time is never counted, even if the user re-enables later this session.
                if (IsEnabled && activity.IsActive(idle))
                {
                    Interlocked.Add(ref activeSecondsSinceHeartbeat, (long)sample.TotalSeconds);
                    Interlocked.Add(ref activeSecondsTotal, (long)sample.TotalSeconds);
                }

                if (++samples < samplesPerHeartbeat) continue;
                samples = 0;
                var active = Interlocked.Exchange(ref activeSecondsSinceHeartbeat, 0);
                if (active > 0)
                    Track(TelemetrySchema.Events.Heartbeat, new Dictionary<string, object?> { ["active_s"] = active });
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    /// <summary>Default kill-switch check: GET options.RemoteConfigUrl → { "enabled": bool }.</summary>
    public static Func<CancellationToken, Task<bool>>? CreateRemoteConfigCheck(
        System.Net.Http.HttpClient http, TelemetryOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RemoteConfigUrl)) return null;
        return async ct =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var json = await http.GetStringAsync(options.RemoteConfigUrl, timeout.Token).ConfigureAwait(false);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                return !doc.RootElement.TryGetProperty("enabled", out var enabled) ||
                       enabled.ValueKind != System.Text.Json.JsonValueKind.False;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return true; // unreachable config never disables telemetry
            }
        };
    }
}
