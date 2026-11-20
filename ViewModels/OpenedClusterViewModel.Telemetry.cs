using System.Threading;
using KafkaLens.ViewModels.Telemetry;

namespace KafkaLens.ViewModels;

/// <summary>Usage telemetry for an open cluster tab. Never records topic, message or filter content.</summary>
public partial class OpenedClusterViewModel
{
    private Task Tracked(string feature, Func<Task> action)
    {
        telemetry.Feature(feature);
        return action();
    }

    private void OnCurrentMessagesPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (applyingTabState) return;
        var (feature, value) = e.PropertyName switch
        {
            nameof(MessagesViewModel.PositiveFilter) => (TelemetrySchema.Features.PositiveFilter, CurrentMessages.PositiveFilter),
            nameof(MessagesViewModel.NegativeFilter) => (TelemetrySchema.Features.NegativeFilter, CurrentMessages.NegativeFilter),
            nameof(MessagesViewModel.LineFilter) => (TelemetrySchema.Features.LineFilter, CurrentMessages.LineFilter),
            _ => (null, null)
        };
        // Once per fetch result per filter kind; filter text is never sent.
        if (feature != null && !string.IsNullOrEmpty(value) && filtersTrackedForFetch.Add(feature))
            telemetry.Feature(feature);
    }

    partial void OnMessagesSortColumnChanged(string? value)
    {
        if (!applyingTabState && value != null) telemetry.Feature(TelemetrySchema.Features.SortMessages);
    }

    partial void OnIsNavigatorOpenChanged(bool value) => telemetry.Feature(TelemetrySchema.Features.NavigatorToggle);

    partial void OnIsFetchPanelOpenChanged(bool value) => telemetry.Feature(TelemetrySchema.Features.FetchPanelToggle);

    private void TrackTopicFilter()
    {
        if (topicFilterTracked || applyingTabState || string.IsNullOrEmpty(FilterText)) return;
        topicFilterTracked = true;
        telemetry.Feature(TelemetrySchema.Features.TopicFilter);
    }

    private void TrackFetchCompleted(int received, string result)
    {
        var props = Interlocked.Exchange(ref activeFetchTelemetry, null);
        if (props == null) return;
        props["received"] = TelemetryBuckets.Count(received);
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(activeFetchStartedTimestamp);
        props["duration"] = TelemetryBuckets.Duration(elapsed);
        props["duration_ms"] = TelemetryBuckets.Milliseconds(elapsed);
        props["result"] = result;
        telemetry.Track(TelemetrySchema.Events.FetchCompleted, props);
    }

    internal void TrackFormatterSelected(string? formatterName, string target, bool auto)
    {
        if (!telemetry.IsEnabled || formatterName == null) return;
        var builtIn = new HashSet<string>(target == "key"
            ? formatterService.GetBuiltInKeyFormatterNames()
            : formatterService.GetBuiltInFormatterNames(), StringComparer.Ordinal);
        telemetry.Track(TelemetrySchema.Events.FormatterSelected, new Dictionary<string, object?>
        {
            ["formatter"] = TelemetryExtensions.FormatterName(formatterName, builtIn),
            ["target"] = target,
            ["auto"] = auto,
        });
    }
}
