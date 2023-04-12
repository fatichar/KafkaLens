using Event.Common;
using Gnmi;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Newtonsoft.Json;
using Path = Gnmi.Path;

namespace KafkaLens.TaranaFormatters;

public class EventFormatter : Formatting.IMessageFormatter
{
    public string Name => "Event";
    
    private readonly JsonFormatter compactFormatter = new(JsonFormatter.Settings.Default);
    private readonly JsonFormatter uglyFormatter = new(JsonFormatter.Settings.Default.WithIndentation());
    
    private readonly JsonSerializerSettings prettySettings = new()
    {
        Formatting = Newtonsoft.Json.Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore,
        ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
    };
    
    public string? Format(byte[] data, bool prettyPrint)
    {
        try {
            var message = Event.Common.Event.Parser.ParseFrom(data);
            
            JsonFormatter formatter = prettyPrint ? uglyFormatter : compactFormatter;
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
            var message = Event.Common.Event.Parser.ParseFrom(data);
            Filter(message, searchText);

            var ugly = uglyFormatter.Format(message);
            var pretty = JsonConvert.SerializeObject(JsonConvert.DeserializeObject(ugly), prettySettings);
            return pretty;
        } catch (InvalidProtocolBufferException e)
        {
            return null;
        }
    }

    private void Filter(Event.Common.Event message, string searchText)
    {
        if (string.IsNullOrEmpty(searchText))
        {
            return;
        }
        searchText = searchText.ToLowerInvariant();

        if (message.Data == null)
        {
            return;
        }

        Filter(message.Data, searchText);
        if (message.Data.Data.Count == 0)
        {
            message.Data = null;
        }
    }

    private static void Filter(AnyData data, string searchText)
    {
        var fields = data.Data;
        MapField<string, AnyValue> originalData = fields.Clone();

        foreach (var pair in originalData)
        {
            if (Matches(pair.Key, searchText))
            {
                continue;
            }

            AnyValue value = pair.Value;

            if (value.Value is not null)
            {
                if (!Matches(value.Value, searchText))
                {
                    fields.Remove(pair.Key);
                }
            }
            else if (value.Data is not null)
            {
                Filter(fields[pair.Key].Data, searchText);
                if (fields[pair.Key].Data.Data.Count == 0)
                {
                    fields.Remove(pair.Key);
                }
            }
        }
    }

    private static bool Matches(TypedValue value, string searchText)
    {
        return Matches(value.ToString(), searchText);
    }

    private static bool Matches(Path prefix, string searchText)
    {
        var pathStr = prefix.ToString();
        return Matches(pathStr, searchText);
    }

    private static bool Matches(string text, string searchText)
    {
        return text.ToLowerInvariant().Contains(searchText);
    }
}