using KafkaLens.Formatting;
using KafkaLens.Shared.Models;
using Newtonsoft.Json;
using System.IO;

namespace KafkaLens.ViewModels.Services;

public class FormatterService : IFormatterService
{
    public const string UnknownFormatterName = "Unknown";

    public IMessageFormatter? GuessValueFormatter(Message message, IList<string> allowedValueFormatterNames)
    {
        IMessageFormatter? best = null;
        int maxLength = 0;
        var allowedSet = allowedValueFormatterNames.Where(n => n != UnknownFormatterName).ToHashSet(StringComparer.Ordinal);

        // Disable console output, as some formatters may write to it.
        var originalOut = Console.Out;
        Console.SetOut(TextWriter.Null);
        try
        {
            foreach (IMessageFormatter formatter in FormatterFactory.Instance.GetFormatters())
            {
                if (!allowedSet.Contains(formatter.Name))
                {
                    continue;
                }

                try
                {
                    var text = formatter.Format(message.Value ?? Array.Empty<byte>(), true);
                    if (text == null) continue;
                    if (text.Length > maxLength)
                    {
                        maxLength = text.Length;
                        best = formatter;
                    }
                }
                catch
                {
                    // Formatter doesn't support this message type — skip silently
                }
            }
        }
        finally
        {
            // Restore console output.
            Console.SetOut(originalOut);
        }

        return best;
    }

    public IMessageFormatter? GuessKeyFormatter(Message message, IList<string> allowedKeyFormatterNames)
    {
        var configuredNames = allowedKeyFormatterNames
            .Where(n => n != UnknownFormatterName)
            .ToList();

        var prioritized = configuredNames
            .Where(n => n != "Text")
            .Concat(configuredNames.Where(n => n == "Text"));

        foreach (var formatterName in prioritized)
        {
            var formatter = FormatterFactory.Instance.GetFormatter(formatterName);
            if (formatter.Format(message.Key ?? Array.Empty<byte>(), false) != null)
            {
                return formatter;
            }
        }

        return null;
    }

    public string NormalizeFormatterName(string? formatterName, IList<string> allowedNames)
    {
        if (IsUnknownFormatter(formatterName))
        {
            return UnknownFormatterName;
        }

        return allowedNames.Contains(formatterName!)
            ? formatterName!
            : UnknownFormatterName;
    }

    public bool CanApplyFormatterToLoadedMessages(string? formatterName, IList<string> allowedNames)
    {
        return !IsUnknownFormatter(formatterName) &&
               formatterName != "Auto" &&
               allowedNames.Contains(formatterName!);
    }

    public bool IsUnknownFormatter(string? formatterName)
    {
        return string.IsNullOrWhiteSpace(formatterName) ||
               formatterName == "Auto" ||
               formatterName == UnknownFormatterName;
    }

    public string? ToKnownFormatterOrNull(string? formatterName)
    {
        return IsUnknownFormatter(formatterName) ? null : formatterName;
    }

    public string GetDefaultFormatterName() => FormatterFactory.Instance.DefaultFormatter.Name;

    public IList<string> GetAllFormatterNames() =>
        FormatterFactory.Instance.GetFormatters().ConvertAll(f => f.Name);

    public IList<string> GetBuiltInKeyFormatterNames() =>
        FormatterFactory.Instance.GetBuiltInKeyFormatterNames();

    public IList<string> BuildFormatterNames(string? configuredRaw, IList<string> allowed)
    {
        var configured = ParseConfiguredFormatterNames(configuredRaw, allowed);
        var names = configured.Count > 0 ? configured : allowed;
        var result = new List<string>(names.Count + 1) { UnknownFormatterName };
        result.AddRange(names);
        return result;
    }

    private static IList<string> ParseConfiguredFormatterNames(string? configuredRaw, IList<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(configuredRaw))
        {
            return new List<string>();
        }

        var configured = TryParseFormatterList(configuredRaw);
        if (configured.Count == 0)
        {
            return new List<string>();
        }

        var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
        var filtered = new List<string>();
        foreach (var name in configured)
        {
            if (allowedSet.Contains(name) && !filtered.Contains(name))
            {
                filtered.Add(name);
            }
        }

        return filtered;
    }

    private static IList<string> TryParseFormatterList(string configuredRaw)
    {
        try
        {
            var parsed = JsonConvert.DeserializeObject<List<string>>(configuredRaw);
            if (parsed != null)
            {
                return parsed.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            }
        }
        catch
        {
            // Fall back to comma-separated list.
        }

        return configuredRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
    }
}
