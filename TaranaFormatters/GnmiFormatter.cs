using System.Text;
using Gnmi;
using Google.Protobuf;
using Path = Gnmi.Path;

namespace KafkaLens.TaranaFormatters;

public class GnmiFormatter : Formatting.IMessageFormatter
{
    public GnmiFormatter()
    {
        jsonFormatter.AtomicArrays = true;
    }

    public string Name => "Gnmi";
    private readonly KafkaLens.Formatting.JsonFormatter jsonFormatter = new ();

    public string? Format(byte[] data, bool prettyPrint)
    {
        try {
            var message = Gnmi.SubscribeResponse.Parser.ParseFrom(data);
            
            JsonFormatter formatter = new JsonFormatter(
                prettyPrint ? JsonFormatter.Settings.Default.WithIndentation()
                    : JsonFormatter.Settings.Default);
            var formatted = formatter.Format(message);
            
            return formatted;
        } catch (InvalidProtocolBufferException e) {
            Console.WriteLine("Failed to parse message: " + e);
        }
        return null;
    }

    public string? Format(byte[] data, string searchText)
    {
        try {
            var message = Gnmi.SubscribeResponse.Parser.ParseFrom(data);
            Filter(message, searchText);

            JsonFormatter formatter = new(JsonFormatter.Settings.Default.WithIndentation());
            return formatter.Format(message);
        } catch (InvalidProtocolBufferException e)
        {
            return null;
        }
    }

    private void Filter(SubscribeResponse message, string searchText)
    {
        if (string.IsNullOrEmpty(searchText))
        {
            return;
        }
        var notification = message.Update;
        if (notification.Prefix != null)
        {
            if (Matches(notification.Prefix, searchText))
            {
                return;
            }
        }
        var updates = notification.Update.Clone();
        foreach (var update in updates)
        {
            var path = update.Path;
            if (path == null)
            {
                continue;
            }
            var pathStr = path.ToString();
            if (!Matches(pathStr, searchText)
                && !Matches(update.Val.ToString(), searchText))
            {
                notification.Update.Remove(update);
            }
        }
    }

    private bool Matches(Path prefix, string searchText)
    {
        var pathStr = prefix.ToString();
        return Matches(pathStr, searchText);
    }

    private bool Matches(string text, string searchText)
    {
        return text.Contains(searchText);
    }
}