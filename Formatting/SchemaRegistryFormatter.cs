using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Newtonsoft.Json;
using Serilog;

namespace KafkaLens.Formatting;

public class SchemaRegistryFormatter : IMessageFormatter
{
    public string Name => "Schema Registry";

    private static readonly ConcurrentDictionary<string, ISchemaRegistryClient> ClientCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonFormatter jsonFormatter = new();

    /// <summary>
    /// Gets or sets the active Schema Registry URL for the current context/tab.
    /// </summary>
    public static string? ActiveSchemaRegistryUrl { get; set; }

    /// <summary>
    /// Registers or retrieves a cached ISchemaRegistryClient for a given URL.
    /// </summary>
    public static ISchemaRegistryClient GetClient(string url)
    {
        return ClientCache.GetOrAdd(url, u =>
        {
            var config = new SchemaRegistryConfig
            {
                Url = u
            };
            return new CachedSchemaRegistryClient(config);
        });
    }

    /// <summary>
    /// Clears cached Schema Registry clients (e.g. when configuration changes).
    /// </summary>
    public static void ClearCache()
    {
        foreach (var client in ClientCache.Values)
        {
            client.Dispose();
        }
        ClientCache.Clear();
    }

    public string? Format(byte[] data, bool prettyPrint)
    {
        return FormatInternal(data, prettyPrint);
    }

    public string? Format(byte[] data, string searchText, bool useObjectFilter = true)
    {
        var jsonText = FormatInternal(data, prettyPrint: true);
        if (jsonText == null) return null;

        return jsonFormatter.Format(Encoding.UTF8.GetBytes(jsonText), searchText, useObjectFilter);
    }

    private string? FormatInternal(byte[] data, bool prettyPrint)
    {
        if (data == null || data.Length < 5 || data[0] != 0)
        {
            return null;
        }

        var urlsToTry = GetUrlsToTry();
        if (urlsToTry.Count == 0)
        {
            throw new InvalidOperationException("Schema Registry URL is not configured for this cluster.");
        }

        Exception? lastException = null;

        foreach (var url in urlsToTry)
        {
            try
            {
                var client = GetClient(url);
                return DeserializeWithClient(client, data, prettyPrint);
            }
            catch (Exception ex)
            {
                lastException = ex;
                Log.Debug(ex, "Failed to deserialize Schema Registry message using URL {Url}", url);
            }
        }

        if (lastException != null)
        {
            throw new Exception($"Schema Registry deserialization failed: {lastException.Message}", lastException);
        }

        return null;
    }

    private static List<string> GetUrlsToTry()
    {
        var urls = new List<string>();
        if (!string.IsNullOrWhiteSpace(ActiveSchemaRegistryUrl))
        {
            urls.Add(ActiveSchemaRegistryUrl);
        }

        foreach (var key in ClientCache.Keys)
        {
            if (!urls.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                urls.Add(key);
            }
        }

        return urls;
    }

    private string DeserializeWithClient(ISchemaRegistryClient client, byte[] data, bool prettyPrint)
    {
        int schemaId = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(1, 4));
        var schema = client.GetSchemaAsync(schemaId).GetAwaiter().GetResult();

        var schemaType = schema.SchemaType;

        switch (schemaType)
        {
            case SchemaType.Avro:
                return DeserializeAvro(client, data, prettyPrint);

            case SchemaType.Json:
                return DeserializeJsonSchema(data, prettyPrint);

            case SchemaType.Protobuf:
                return DeserializeProtobuf(client, data, schema, prettyPrint);

            default:
                return DeserializeAvro(client, data, prettyPrint);
        }
    }

    private string DeserializeAvro(ISchemaRegistryClient client, byte[] data, bool prettyPrint)
    {
        var deserializer = new AvroDeserializer<Avro.Generic.GenericRecord>(client);
        var record = deserializer.DeserializeAsync(data, isNull: false, SerializationContext.Empty).GetAwaiter().GetResult();

        var formatting = prettyPrint ? Newtonsoft.Json.Formatting.Indented : Newtonsoft.Json.Formatting.None;
        return JsonConvert.SerializeObject(record, formatting);
    }

    private string DeserializeJsonSchema(byte[] data, bool prettyPrint)
    {
        var jsonBytes = data.AsSpan(5).ToArray();
        var jsonText = Encoding.UTF8.GetString(jsonBytes);

        if (!prettyPrint) return jsonText;

        try
        {
            var parsed = JsonConvert.DeserializeObject(jsonText);
            return JsonConvert.SerializeObject(parsed, Newtonsoft.Json.Formatting.Indented);
        }
        catch
        {
            return jsonText;
        }
    }

    private string DeserializeProtobuf(ISchemaRegistryClient client, byte[] data, Schema schema, bool prettyPrint)
    {
        try
        {
            var jsonDeserializer = new JsonDeserializer<object>(client);
            var obj = jsonDeserializer.DeserializeAsync(data, isNull: false, SerializationContext.Empty).GetAwaiter().GetResult();
            var formatting = prettyPrint ? Newtonsoft.Json.Formatting.Indented : Newtonsoft.Json.Formatting.None;
            return JsonConvert.SerializeObject(obj, formatting);
        }
        catch
        {
            var jsonBytes = data.AsSpan(5).ToArray();
            return Encoding.UTF8.GetString(jsonBytes);
        }
    }
}
