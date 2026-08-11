using KafkaLens.Shared.Models;
using Serilog;

namespace KafkaLens.ViewModels;

public partial class OpenedClusterViewModel
{
    internal Task SaveTopicSettingsAsync()
    {
        if (SelectedNode is not IMessageSource node) return Task.CompletedTask;

        var topicName = GetCurrentTopicName();

        TryGuessUnknownFormattersFromLoadedMessages(node, topicName);

        var settings = new TopicSettings
        {
            KeyFormatter = formatterService.ToKnownFormatterOrNull(node.KeyFormatterName),
            ValueFormatter = formatterService.ToKnownFormatterOrNull(node.FormatterName)
        };
        topicSettingsService.SetSettings(cluster.Id, topicName, settings, ApplyToAllClusters);

        foreach (var msg in LoadedMessagesForTopic(topicName))
        {
            if (formatterService.CanApplyFormatterToLoadedMessages(settings.ValueFormatter, ValueFormatterNames))
                msg.FormatterName = settings.ValueFormatter!;
            if (formatterService.CanApplyFormatterToLoadedMessages(settings.KeyFormatter, KeyFormatterNames))
                msg.KeyFormatterName = settings.KeyFormatter!;
        }

        return Task.CompletedTask;
    }

    private void TryGuessUnknownFormattersFromLoadedMessages(IMessageSource node, string topicName)
    {
        var firstMessage = LoadedMessagesForTopic(topicName).FirstOrDefault()?.Message;
        if (firstMessage == null) return;

        if (formatterService.IsUnknownFormatter(node.FormatterName))
        {
            var formatter = formatterService.GuessValueFormatter(firstMessage, ValueFormatterNames);
            node.FormatterName = formatter?.Name ?? formatterService.GetDefaultFormatterName();
            Log.Information("Guessed value formatter {Formatter} for topic {Topic}", node.FormatterName, topicName);
        }

        if (formatterService.IsUnknownFormatter(node.KeyFormatterName))
        {
            var formatter = formatterService.GuessKeyFormatter(firstMessage, KeyFormatterNames);
            if (formatter != null)
            {
                node.KeyFormatterName = formatter.Name;
                Log.Information("Guessed key formatter {Formatter} for topic {Topic}", node.KeyFormatterName, topicName);
            }
        }
    }

    private void GuessFormatterForSelectedNode(bool isKeyFormatter)
    {
        if (SelectedNode is not IMessageSource node) return;

        var topicName = GetTopicName(node);
        if (topicName == null) return;

        var topicMessages = LoadedMessagesForTopic(topicName).ToList();
        if (topicMessages.Count == 0) return;

        var firstMessage = topicMessages[0].Message;

        if (isKeyFormatter)
        {
            var keyFormatter = formatterService.GuessKeyFormatter(firstMessage, KeyFormatterNames)?.Name;
            if (string.IsNullOrWhiteSpace(keyFormatter)) return;

            node.KeyFormatterName = keyFormatter;
            foreach (var msg in topicMessages)
                msg.KeyFormatterName = keyFormatter;
            return;
        }

        var valueFormatter = formatterService.GuessValueFormatter(firstMessage, ValueFormatterNames)?.Name
            ?? formatterService.GetDefaultFormatterName();
        node.FormatterName = valueFormatter;
        foreach (var msg in topicMessages)
            msg.FormatterName = valueFormatter;
    }
}
