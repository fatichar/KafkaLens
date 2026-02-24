using System.Text;

namespace KafkaLens.Formatting;

public class TextFormatter : IMessageFormatter
{
    public string? Format(byte[] data, bool prettyPrint)
    {
        return Encoding.UTF8.GetString(data);
    }

    public string? Format(byte[] data, string searchText, bool useObjectFilter = false)
    {
        var text = Format(data, true);
        if (text == null || string.IsNullOrWhiteSpace(searchText))
        {
            return text;
        }

        if (useObjectFilter)
        {
            return text.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ? text : string.Empty;
        }

        // Line-filter mode: return only lines that contain the search text (case-insensitive)
        var lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var filtered = lines.Where(line => !string.IsNullOrEmpty(line) &&
                                           line.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);
        return string.Join(Environment.NewLine, filtered);
    }

    public string Name => "Text";
}