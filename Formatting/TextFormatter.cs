using System.Text;

namespace KafkaLens.Formatting;

public class TextFormatter : IMessageFormatter
{
    public string? Format(byte[] data, bool prettyPrint)
    {
        return Encoding.UTF8.GetString(data);
    }

    public string? Format(byte[] data, string searchText)
    {
        var text = Format(data, false) ?? string.Empty;
        var lines = text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        var filteredLines = lines.Where(line => line.Contains(searchText));
        var filteredText = string.Join(Environment.NewLine, filteredLines);
        return filteredText;
    }

    public string Name => "Text";
}