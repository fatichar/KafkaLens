using System.Text;
using Google.Protobuf;

namespace KafkaLens.TaranaFormatters;

public class GnmiFormatter : Formatting.IMessageFormatter
{
    public string Name => "Gnmi";
    private KafkaLens.Formatting.JsonFormatter jsonFormatter = new ();

    public string? Format(byte[] data, bool prettyPrint)
    {
        Gnmi.SubscribeResponse message;
        try {
            Console.WriteLine("parsing message");
            message = Gnmi.SubscribeResponse.Parser.ParseFrom(data);
            
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
        var jsonText = Format(data, false);
        if (jsonText == null) {
            return null;
        }
        return jsonFormatter.Format(Encoding.UTF8.GetBytes(jsonText), searchText);
    }
}