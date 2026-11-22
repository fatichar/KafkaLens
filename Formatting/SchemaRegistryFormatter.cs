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
        CancellationToken cancellationToken = default)
    {
        if (!HasSchemaId(data)) return Task.FromResult<string?>(null);
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("Schema Registry URL is not configured for this cluster.");

        var formatter = Formatters.GetOrAdd(url, address => new Lazy<SchemaRegistryFormatter>(() =>
            new(new CachedSchemaRegistryClient(new SchemaRegistryConfig { Url = address })))).Value;
        return formatter.DecodeAsync(data, topic, isKey, cancellationToken);
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
            SchemaType.Avro => await DecodeAvroAsync(data!, topic, isKey, cancellationToken).ConfigureAwait(false),
            SchemaType.Json => JsonConvert.SerializeObject(JsonConvert.DeserializeObject(
                Utf8.GetString(data!.AsSpan(5)))),
            SchemaType.Protobuf => (await protobuf.DecodeAsync(schema, data!, cancellationToken)
                .ConfigureAwait(false)).ToString(Newtonsoft.Json.Formatting.None),
            _ => throw new NotSupportedException($"Unsupported Schema Registry type {schema.SchemaType}.")
        };
    }

    private static bool HasSchemaId(byte[]? data) => data is { Length: >= 5 } && data[0] == 0;

    private async Task<string> DecodeAvroAsync(byte[] data, string topic, bool isKey, CancellationToken cancellationToken)
    {
        var context = new SerializationContext(isKey ? MessageComponentType.Key : MessageComponentType.Value, topic);
        var record = await avro.DeserializeAsync(data, false, context)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        return JsonConvert.SerializeObject(ToJsonCompatibleValue(record));
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
