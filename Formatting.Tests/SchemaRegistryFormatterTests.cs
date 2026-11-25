using System;
using System.Buffers.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Confluent.SchemaRegistry;
using FluentAssertions;
using KafkaLens.Formatting;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace KafkaLens.Formatting.Tests;

public class SchemaRegistryFormatterTests
{
    [Fact]
    public async Task DecodeAsync_IgnoresDataWithoutConfluentFraming()
    {
        var client = Substitute.For<ISchemaRegistryClient>();
        var formatter = new SchemaRegistryFormatter(client);

        (await formatter.DecodeAsync(null, "topic", false)).Should().BeNull();
        (await formatter.DecodeAsync([0, 1, 2, 3], "topic", false)).Should().BeNull();
        (await formatter.DecodeAsync([1, 0, 0, 0, 1, 0], "topic", false)).Should().BeNull();
        _ = client.DidNotReceiveWithAnyArgs().GetSchemaAsync(default);
    }

    [Fact]
    public async Task DecodeAsync_UsesOnlyTheSuppliedRegistry()
    {
        var jsonClient = Substitute.For<ISchemaRegistryClient>();
        var avroClient = Substitute.For<ISchemaRegistryClient>();
        jsonClient.GetSchemaAsync(42).Returns(Task.FromResult(new Schema("{}", SchemaType.Json)));
        avroClient.GetSchemaAsync(42).Returns(Task.FromException<Schema>(new InvalidOperationException("Unknown schema")));
        var payload = Frame(42, Encoding.UTF8.GetBytes("""{"test":"value"}"""));

        var decoded = await new SchemaRegistryFormatter(jsonClient).DecodeAsync(payload, "topic", false);
        JObject.Parse(decoded!)["test"]!.Value<string>().Should().Be("value");
        Func<Task> decode = () => new SchemaRegistryFormatter(avroClient).DecodeAsync(payload, "topic", false);
        await decode.Should().ThrowAsync<InvalidOperationException>().WithMessage("Unknown schema");
    }

    [Fact]
    public async Task DecodeAsync_AvroDisplaysRecordFields()
    {
        var client = Substitute.For<ISchemaRegistryClient>();
        var schema = new Schema("""
            {"type":"record","name":"Outer","fields":[{"name":"nested","type":{
                "type":"record","name":"Inner","fields":[{"name":"text","type":"string"}]}}]}
            """, SchemaType.Avro);
        client.GetSchemaAsync(42, Arg.Any<string>()).Returns(Task.FromResult(schema));
        client.GetSchemaBySubjectAndIdAsync(Arg.Any<string>(), 42, Arg.Any<string>()).Returns(Task.FromResult(schema));

        var decoded = await new SchemaRegistryFormatter(client).DecodeAsync(
            Frame(42, [4, (byte)'h', (byte)'i']), "topic", false);

        var json = JObject.Parse(decoded!);
        json["nested"]!["text"]!.Value<string>().Should().Be("hi");
        json["Schema"].Should().BeNull();
    }

    [Fact]
    public async Task DecodeAsync_ProtobufResolvesImportsAndNestedMessageIndexes()
    {
        var client = Substitute.For<ISchemaRegistryClient>();
        var root = new Schema("""
            syntax = "proto3";
            package example;
            import "common.proto";
            message Unused {}
            message Envelope {
                message Nested {
                    common.Imported item = 1;
                    repeated int32 numbers = 2;
                    map<string, bool> flags = 3;
                }
            }
            """, [new SchemaReference("common.proto", "common-subject", 1)], SchemaType.Protobuf);
        var dependency = new RegisteredSchema("common-subject", 1, 43, """
            syntax = "proto3";
            package common;
            message Imported { string text = 1; }
            """, SchemaType.Protobuf, []);
        client.GetSchemaAsync(42).Returns(Task.FromResult(root));
        client.GetRegisteredSchemaAsync("common-subject", 1).Returns(Task.FromResult(dependency));

        var decoded = await new SchemaRegistryFormatter(client).DecodeAsync(Frame(42,
            [4, 2, 0, 0x0A, 4, 0x0A, 2, (byte)'h', (byte)'i', 0x12, 2, 1, 2,
                0x1A, 5, 0x0A, 1, (byte)'x', 0x10, 1]), "topic", false);

        var json = JObject.Parse(decoded!);
        json["item"]!["text"]!.Value<string>().Should().Be("hi");
        json["numbers"]!.Values<int>().Should().Equal(1, 2);
        json["flags"]!["x"]!.Value<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task DecodeAsync_CancelsWhileRegistryRequestIsPending()
    {
        var client = Substitute.For<ISchemaRegistryClient>();
        var pending = new TaskCompletionSource<Schema>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.GetSchemaAsync(42).Returns(pending.Task);
        using var cancellation = new CancellationTokenSource();
        var decoding = new SchemaRegistryFormatter(client).DecodeAsync(Frame(42, []), "topic", false, cancellation.Token);

        decoding.IsCompleted.Should().BeFalse();
        cancellation.Cancel();
        Func<Task> wait = () => decoding;
        await wait.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ISchemaRegistryClient ClientWith(int id, Schema schema)
    {
        var client = Substitute.For<ISchemaRegistryClient>();
        client.GetSchemaAsync(id, Arg.Any<string>()).Returns(Task.FromResult(schema));
        client.GetSchemaAsync(id).Returns(Task.FromResult(schema));
        client.GetSchemaBySubjectAndIdAsync(Arg.Any<string>(), id, Arg.Any<string>()).Returns(Task.FromResult(schema));
        return client;
    }

    [Fact]
    public async Task DecodeAsync_AvroPrimitiveKey()
    {
        var client = ClientWith(7, new Schema("\"string\"", SchemaType.Avro));

        var decoded = await new SchemaRegistryFormatter(client).DecodeAsync(
            Frame(7, [10, (byte)'o', (byte)'r', (byte)'d', (byte)'e', (byte)'r']), "topic", true);

        decoded.Should().Be("\"order\"");
    }

    [Fact]
    public async Task DecodeAsync_AvroEnumAndArrayRoots()
    {
        var enumClient = ClientWith(8, new Schema("""{"type":"enum","name":"Status","symbols":["NEW","PAID"]}""", SchemaType.Avro));
        (await new SchemaRegistryFormatter(enumClient).DecodeAsync(Frame(8, [2]), "topic", false)).Should().Be("\"PAID\"");

        var arrayClient = ClientWith(9, new Schema("""{"type":"array","items":"long"}""", SchemaType.Avro));
        // block of 2 items (zigzag 4), values 1 (2) and 2 (4), end block 0
        (await new SchemaRegistryFormatter(arrayClient).DecodeAsync(Frame(9, [4, 2, 4, 0]), "topic", false)).Should().Be("[1,2]");
    }

    [Fact]
    public async Task DecodeAsync_JsonSchemaPreservesStringsAndNumbers()
    {
        var client = ClientWith(11, new Schema("{}", SchemaType.Json));
        var payload = Frame(11, Encoding.UTF8.GetBytes("""{"at":"2026-01-01T12:00:00+05:00","amount":10.50}"""));

        var decoded = await new SchemaRegistryFormatter(client).DecodeAsync(payload, "topic", false);

        decoded.Should().Be("""{"at":"2026-01-01T12:00:00+05:00","amount":10.50}""");
    }

    private static byte[] Frame(int id, byte[] payload)
    {
        var buffer = new byte[5 + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(1, 4), id);
        payload.CopyTo(buffer, 5);
        return buffer;
    }
}
