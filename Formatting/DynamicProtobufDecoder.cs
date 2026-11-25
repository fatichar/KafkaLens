extern alias ProtoSchema;

using System.Globalization;
using System.Runtime.CompilerServices;
using Confluent.SchemaRegistry;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Newtonsoft.Json.Linq;
using ProtoParser = ProtoSchema::Google.Protobuf.Reflection;

namespace KafkaLens.Formatting;

internal sealed class DynamicProtobufDecoder(ISchemaRegistryClient client)
{
    private const string RootFileName = "kafkalens-message.proto";
    private const int MaxDepth = 64;
    private readonly ConditionalWeakTable<Schema, Lazy<Task<FileDescriptor>>> descriptors = new();

    public async Task<JObject> DecodeAsync(Schema schema, byte[] data, CancellationToken cancellationToken)
    {
        var lookup = descriptors.GetValue(schema, key => new Lazy<Task<FileDescriptor>>(() => ParseSchemaAsync(key)));
        var task = lookup.Value;
        FileDescriptor root;
        try
        {
            root = await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (task.IsFaulted)
        {
            descriptors.Remove(schema, out _);
            throw;
        }

        using var input = new CodedInputStream(data, 5, data.Length - 5);
        int count = input.ReadSInt32();
        if (count < 0 || count > MaxDepth)
            throw new InvalidDataException("Invalid Protobuf message index count.");

        // Confluent encodes the common path [0] as a single zero.
        var descriptor = MessageAt(root.MessageTypes, count == 0 ? 0 : input.ReadSInt32());
        for (int i = 1; i < count; i++)
            descriptor = MessageAt(descriptor.NestedTypes, input.ReadSInt32());

        return ReadMessage(input, descriptor, 0);
    }

    private async Task<FileDescriptor> ParseSchemaAsync(Schema root)
    {
        var set = new ProtoParser.FileDescriptorSet { FileSystem = new RegistryOnlyFileSystem() };
        var sources = new Dictionary<string, (string Subject, int Version)>(StringComparer.Ordinal);
        await AddSourceAsync(set, RootFileName, root, sources, 0).ConfigureAwait(false);
        set.Process();
        if (set.GetErrors().Any(error => error.IsError))
            throw new InvalidDataException("The registered Protobuf schema or one of its references could not be parsed.");

        set.ApplyFileDependencyOrder();
        var serialized = set.Files.Select(file =>
        {
            using var stream = new MemoryStream();
            ProtoBuf.Serializer.Serialize(stream, file);
            return ByteString.CopyFrom(stream.ToArray());
        });
        return FileDescriptor.BuildFromByteStrings(serialized).Single(file => file.Name == RootFileName);
    }

    private async Task AddSourceAsync(ProtoParser.FileDescriptorSet set, string name, Schema schema,
        Dictionary<string, (string Subject, int Version)> sources, int depth)
    {
        if (depth >= MaxDepth)
            throw new InvalidDataException("The Protobuf schema references are nested too deeply.");

        using var reader = new StringReader(schema.SchemaString);
        set.Add(name, includeInOutput: true, reader);
        foreach (var reference in schema.References ?? [])
        {
            var target = (reference.Subject, reference.Version);
            if (sources.TryGetValue(reference.Name, out var existing))
            {
                if (existing != target)
                    throw new InvalidDataException($"Conflicting versions of Protobuf import '{reference.Name}'.");
                continue;
            }

            if (reference.Name == RootFileName)
                throw new InvalidDataException("A Protobuf reference conflicts with the root schema name.");
            sources.Add(reference.Name, target);
            var dependency = await client.GetRegisteredSchemaAsync(reference.Subject, reference.Version).ConfigureAwait(false);
            await AddSourceAsync(set, reference.Name, dependency, sources, depth + 1).ConfigureAwait(false);
        }
    }

    private static MessageDescriptor MessageAt(IList<MessageDescriptor> types, int index) =>
        index >= 0 && index < types.Count
            ? types[index]
            : throw new InvalidDataException("The Protobuf message index does not exist in the registered schema.");

    private static JObject ReadMessage(CodedInputStream input, MessageDescriptor descriptor, int depth,
        JObject? result = null, int endGroup = 0)
    {
        if (depth >= MaxDepth)
            throw new InvalidDataException("The Protobuf message is nested too deeply.");

        result ??= new JObject();
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            int number = WireFormat.GetTagFieldNumber(tag);
            var wireType = WireFormat.GetTagWireType(tag);
            if (wireType == WireFormat.WireType.EndGroup)
            {
                if (number != endGroup) throw new InvalidDataException("Unexpected Protobuf end-group tag.");
                return result;
            }

            var field = descriptor.FindFieldByNumber(number);
            if (field == null)
            {
                input.SkipLastField();
                continue;
            }

            var name = field.JsonName;
            var expectedWireType = WireType(field.FieldType);
            if (field.IsMap && wireType == WireFormat.WireType.LengthDelimited)
            {
                using var item = input.ReadBytes().CreateCodedInput();
                var entry = ReadMessage(item, field.MessageType, depth + 1);
                var key = field.MessageType.FindFieldByNumber(1);
                var value = field.MessageType.FindFieldByNumber(2);
                var map = result[name] as JObject;
                if (map == null) result[name] = map = new JObject();
                map[MapKey(entry[key.JsonName] ?? DefaultValue(key))] = entry[value.JsonName] ?? DefaultValue(value);
            }
            else if (field.IsRepeated && wireType == WireFormat.WireType.LengthDelimited &&
                     expectedWireType != WireFormat.WireType.LengthDelimited && field.FieldType != FieldType.Group)
            {
                using var packed = input.ReadBytes().CreateCodedInput();
                var list = GetList(result, name);
                while (!packed.IsAtEnd)
                    list.Add(ReadValue(packed, field, depth));
            }
            else if (wireType == expectedWireType)
            {
                if (field.IsRepeated)
                {
                    GetList(result, name).Add(ReadValue(input, field, depth));
                    continue;
                }

                if (field.ContainingOneof is { } oneof)
                    foreach (var sibling in oneof.Fields)
                        if (sibling != field) result.Remove(sibling.JsonName);

                var previous = result[name] as JObject;
                var value = ReadValue(input, field, depth, previous);
                if (!ReferenceEquals(previous, value)) result[name] = value;
            }
            else
            {
                input.SkipLastField();
            }
        }

        if (endGroup != 0) throw new InvalidDataException("A Protobuf group was not terminated.");
        return result;
    }

    private static JArray GetList(JObject result, string name)
    {
        if (result[name] is JArray list) return list;
        list = new JArray();
        result[name] = list;
        return list;
    }

    private static WireFormat.WireType WireType(FieldType type) => type switch
    {
        FieldType.Double or FieldType.Fixed64 or FieldType.SFixed64 => WireFormat.WireType.Fixed64,
        FieldType.Float or FieldType.Fixed32 or FieldType.SFixed32 => WireFormat.WireType.Fixed32,
        FieldType.String or FieldType.Bytes or FieldType.Message => WireFormat.WireType.LengthDelimited,
        FieldType.Group => WireFormat.WireType.StartGroup,
        _ => WireFormat.WireType.Varint
    };

    private static JToken ReadValue(CodedInputStream input, FieldDescriptor field, int depth,
        JObject? previous = null)
    {
        switch (field.FieldType)
        {
            case FieldType.Message:
                using (var nested = input.ReadBytes().CreateCodedInput())
                    return ReadMessage(nested, field.MessageType, depth + 1, previous);
            case FieldType.Group:
                return ReadMessage(input, field.MessageType, depth + 1, previous, field.FieldNumber);
            case FieldType.Enum:
                var number = input.ReadEnum();
                return field.EnumType.FindValueByNumber(number) is { } value ? new JValue(value.Name) : new JValue(number);
            default:
                return new JValue(field.FieldType switch
                {
                    FieldType.Bool => input.ReadBool(),
                    FieldType.String => input.ReadString(),
                    FieldType.Bytes => input.ReadBytes().ToBase64(),
                    FieldType.Int32 => input.ReadInt32(),
                    FieldType.UInt32 => input.ReadUInt32(),
                    FieldType.SInt32 => input.ReadSInt32(),
                    FieldType.Fixed32 => input.ReadFixed32(),
                    FieldType.SFixed32 => input.ReadSFixed32(),
                    FieldType.Int64 => input.ReadInt64().ToString(CultureInfo.InvariantCulture),
                    FieldType.UInt64 => input.ReadUInt64().ToString(CultureInfo.InvariantCulture),
                    FieldType.SInt64 => input.ReadSInt64().ToString(CultureInfo.InvariantCulture),
                    FieldType.Fixed64 => input.ReadFixed64().ToString(CultureInfo.InvariantCulture),
                    FieldType.SFixed64 => input.ReadSFixed64().ToString(CultureInfo.InvariantCulture),
                    FieldType.Double => input.ReadDouble(),
                    FieldType.Float => input.ReadFloat(),
                    _ => throw new NotSupportedException($"Unsupported Protobuf field type {field.FieldType}.")
                });
        }
    }

    private static JToken DefaultValue(FieldDescriptor field) => field.FieldType switch
    {
        FieldType.Bool => new JValue(false),
        FieldType.String or FieldType.Bytes => new JValue(""),
        FieldType.Enum => new JValue(field.EnumType.Values[0].Name),
        FieldType.Message => new JObject(),
        FieldType.Int64 or FieldType.UInt64 or FieldType.SInt64 or FieldType.Fixed64 or FieldType.SFixed64 => new JValue("0"),
        _ => new JValue(0)
    };

    private static string MapKey(JToken key) => key.Type == JTokenType.String
        ? key.Value<string>()!
        : key.ToString(Newtonsoft.Json.Formatting.None);

    private sealed class RegistryOnlyFileSystem : ProtoParser.IFileSystem
    {
        public bool Exists(string path) => false;
        public TextReader OpenText(string path) => throw new FileNotFoundException();
    }
}
