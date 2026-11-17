using System.IO;
using Serilog;

namespace KafkaLens.ViewModels.Telemetry;

/// <summary>
/// Small on-disk queue (JSON lines) for events that could not be sent: offline, endpoint down,
/// or the process is crashing. Bounded by size and age; oldest entries are dropped first.
/// All operations are short synchronous file IO and are serialized with a lock.
/// </summary>
public sealed class TelemetrySpool
{
    private readonly string filePath;
    private readonly long maxBytes;
    private readonly TimeSpan maxAge;
    private readonly Func<DateTimeOffset> now;
    private readonly object gate = new();

    public TelemetrySpool(string filePath, long maxBytes, TimeSpan maxAge, Func<DateTimeOffset>? now = null)
    {
        this.filePath = filePath;
        this.maxBytes = maxBytes;
        this.maxAge = maxAge;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public string FilePath => filePath;

    public void Append(IEnumerable<TelemetryEvent> events)
    {
        lock (gate)
        {
            try
            {
                var lines = events.Select(e => e.ToJson()).ToList();
                if (lines.Count == 0) return;
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                File.AppendAllLines(filePath, lines);
                EnforceSizeLimit();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Telemetry spool append failed");
            }
        }
    }

    /// <summary>Returns all unexpired events and empties the spool.</summary>
    public IReadOnlyList<TelemetryEvent> Drain()
    {
        lock (gate)
        {
            try
            {
                if (!File.Exists(filePath)) return Array.Empty<TelemetryEvent>();
                var cutoff = now() - maxAge;
                var events = File.ReadAllLines(filePath)
                    .Select(TelemetryEvent.FromJson)
                    .Where(e => e != null && e.Timestamp >= cutoff)
                    .Select(e => e!)
                    .ToList();
                File.Delete(filePath);
                return events;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Telemetry spool drain failed");
                return Array.Empty<TelemetryEvent>();
            }
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            try
            {
                if (File.Exists(filePath)) File.Delete(filePath);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Telemetry spool clear failed");
            }
        }
    }

    private void EnforceSizeLimit()
    {
        var info = new FileInfo(filePath);
        if (!info.Exists || info.Length <= maxBytes) return;

        // Keep the newest lines that fit in half the budget, so we don't rewrite on every append.
        var lines = File.ReadAllLines(filePath);
        var kept = new List<string>();
        long size = 0;
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            size += lines[i].Length + 1;
            if (size > maxBytes / 2) break;
            kept.Add(lines[i]);
        }
        kept.Reverse();
        File.WriteAllLines(filePath, kept);
    }
}
