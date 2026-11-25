using System.Buffers.Binary;
using System.Collections;
using System.Collections.Concurrent;
using System.Text;
using Avro.Generic;
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Newtonsoft.Json;

namespace KafkaLens.Formatting;

public sealed class SchemaRegistryFormatter
{
    public const string Name = "Schema Registry";

    private static readonly ConcurrentDictionary<string, Lazy<SchemaRegistryFormatter>> Formatters =
        new(StringComparer.Ordinal);
    private static readonly UTF8Encoding Utf8 = new(false, true);

    private readonly ISchemaRegistryClient client;
    private readonly DynamicProtobufDecoder protobuf;
    private readonly AvroDeserializer<GenericRecord> avro;

    public SchemaRegistryFormatter(ISchemaRegistryClient client)
    {
        this.client = client;
        protobuf = new DynamicProtobufDecoder(client);
        avro = new AvroDeserializer<GenericRecord>(client);
    }

    public static Task<string?> DecodeAsync(string? url, byte[]? data, string topic, bool isKey,
        CancellationToken cancellationToken = default) =>
        DecodeAsync(string.IsNullOrWhiteSpace(url) ? null : new SchemaRegistryOptions(url), data, topic, isKey, cancellationToken);

    /// <summary>
    /// Decodes Confluent wire-format data. One cached client (schemas are cached by Confluent's
    /// <see cref="CachedSchemaRegistryClient"/>) is kept per distinct registry connection.
    /// Returns null when the data does not carry a schema id.
    /// </summary>
    public static Task<string?> DecodeAsync(SchemaRegistryOptions? options, byte[]? data, string topic, bool isKey,
        CancellationToken cancellationToken = default)
    {
        if (!HasSchemaId(data)) return Task.FromResult<string?>(null);
        if (options == null || string.IsNullOrWhiteSpace(options.Url))
            throw new InvalidOperationException("Schema Registry URL is not configured for this cluster.");

        var formatter = Formatters.GetOrAdd(options.CacheKey(), _ => new Lazy<SchemaRegistryFormatter>(() =>
            new(new CachedSchemaRegistryClient(options.ToConfig())))).Value;
        return formatter.DecodeAsync(data, topic, isKey, cancellationToken);
    }

    /// <summary>Checks that the registry is reachable and the credentials are accepted.</summary>
    public static async Task<int> TestConnectionAsync(SchemaRegistryOptions options, CancellationToken cancellationToken = default)
    {
        using var client = new CachedSchemaRegistryClient(options.ToConfig());
        var subjects = await client.GetAllSubjectsAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        return subjects.Count;
    }

    public async Task<string?> DecodeAsync(byte[]? data, string topic, bool isKey,
        CancellationToken cancellationToken = default)
    {
        if (!HasSchemaId(data)) return null;
        cancellationToken.ThrowIfCancellationRequested();

        int id = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(1, 4));
        var schema = await client.GetSchemaAsync(id).WaitAsync(cancellationToken).ConfigureAwait(false);

        return schema.SchemaType switch
        {
            SchemaType.Avro => await DecodeAvroAsync(schema, data!, topic, isKey, cancellationToken).ConfigureAwait(false),
            SchemaType.Json => NormalizeJson(Utf8.GetString(data!.AsSpan(5))),
            SchemaType.Protobuf => (await protobuf.DecodeAsync(schema, data!, cancellationToken)
                .ConfigureAwait(false)).ToString(Newtonsoft.Json.Formatting.None),
            _ => throw new NotSupportedException($"Unsupported Schema Registry type {schema.SchemaType}.")
        };
    }

    private static bool HasSchemaId(byte[]? data) => data is { Length: >= 5 } && data[0] == 0;

    /// <summary>Re-serializes JSON compactly without interpreting values (no date or float conversion).</summary>
    private static string NormalizeJson(string json)
    {
        using var reader = new JsonTextReader(new StringReader(json))
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Decimal
        };
        return Newtonsoft.Json.Linq.JToken.ReadFrom(reader).ToString(Newtonsoft.Json.Formatting.None);
    }

    private async Task<string> DecodeAvroAsync(Schema schema, byte[] data, string topic, bool isKey, CancellationToken cancellationToken)
    {
        // Non-record root schemas (string/long keys, enums, arrays, maps, unions) cannot be read as GenericRecord.
        if (TryParseAvro(schema.SchemaString) is { } writer && writer.Tag != Avro.Schema.Type.Record && writer.Tag != Avro.Schema.Type.Error)
        {
            using var stream = new MemoryStream(data, 5, data.Length - 5, writable: false);
            var value = new GenericDatumReader<object>(writer, writer).Read(default!, new Avro.IO.BinaryDecoder(stream));
            return JsonConvert.SerializeObject(ToJsonCompatibleValue(value));
        }


        var context = new SerializationContext(isKey ? MessageComponentType.Key : MessageComponentType.Value, topic);
        var record = await avro.DeserializeAsync(data, false, context)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        return JsonConvert.SerializeObject(ToJsonCompatibleValue(record));
    }

    /// <summary>Parses a self-contained Avro schema; null when it uses references (left to the record deserializer).</summary>
    private static Avro.Schema? TryParseAvro(string schemaString)
    {
        try
        {
            return Avro.Schema.Parse(schemaString);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static object? ToJsonCompatibleValue(object? value) => value switch
    {
        GenericRecord record => record.Schema.Fields.ToDictionary(field => field.Name,
            field => ToJsonCompatibleValue(record.GetValue(field.Pos))),
        GenericEnum item => item.Value,
        GenericFixed item => item.Value,
        IDictionary map => map.Keys.Cast<string>().ToDictionary(key => key, key => ToJsonCompatibleValue(map[key])),
        Array array when value is not byte[] => array.Cast<object?>().Select(ToJsonCompatibleValue).ToArray(),
        _ => value
    };
}
