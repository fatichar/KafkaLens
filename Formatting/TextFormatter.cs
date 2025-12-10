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

        // If line filter is not used, just return if text contains search text
        return text.ToLowerInvariant().Contains(searchText.ToLowerInvariant()) ? text : "";
    }

    public string Name => "Text";
}