using Google.Protobuf;

namespace KafkaLens.TaranaFormatters;

public class GnmiFormatter : Formatting.IMessageFormatter
{
    public string Name => "Gnmi";

    public string? Format(byte[] data)
    {
        Gnmi.SubscribeResponse message;
        try {
            Console.WriteLine("parsing message");
            message = Gnmi.SubscribeResponse.Parser.ParseFrom(data);
            
            JsonFormatter formatter = new JsonFormatter(JsonFormatter.Settings.Default.WithIndentation());
            var formatted = formatter.Format(message);
            
            
            Console.WriteLine("message parsed successfully");
            return formatted;
        } catch (InvalidProtocolBufferException e) {
            Console.WriteLine("Failed to parse message: " + e);
        }
        return null;
    }
}