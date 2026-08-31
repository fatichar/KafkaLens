using System;
using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using KafkaLens.Formatting;
using Xunit;

namespace KafkaLens.Formatting.Tests;

public class SchemaRegistryFormatterTests
{
    private readonly SchemaRegistryFormatter formatter = new();

    [Fact]
    public void Name_ShouldBeSchemaRegistry()
    {
        formatter.Name.Should().Be("Schema Registry");
    }

    [Fact]
    public void FormatterFactory_ShouldIncludeSchemaRegistry()
    {
        var registered = FormatterFactory.Instance.GetFormatter("Schema Registry");
        registered.Should().NotBeNull();
        registered.Should().BeOfType<SchemaRegistryFormatter>();
    }

    [Fact]
    public void Format_NullData_ShouldReturnNull()
    {
        var result = formatter.Format(null!, prettyPrint: true);
        result.Should().BeNull();
    }

    [Fact]
    public void Format_InvalidLength_ShouldReturnNull()
    {
        var shortBytes = new byte[] { 0, 1, 2, 3 }; // < 5 bytes
        var result = formatter.Format(shortBytes, prettyPrint: true);
        result.Should().BeNull();
    }

    [Fact]
    public void Format_InvalidMagicByte_ShouldReturnNull()
    {
        var invalidMagic = new byte[] { 0x01, 0x00, 0x00, 0x00, 0x01, 0x7B, 0x7D }; // Magic byte != 0
        var result = formatter.Format(invalidMagic, prettyPrint: true);
        result.Should().BeNull();
    }

    [Fact]
    public void Format_JsonSchemaPayload_WithoutActiveUrl_ShouldThrowException()
    {
        SchemaRegistryFormatter.ActiveSchemaRegistryUrl = null;
        SchemaRegistryFormatter.ClearCache();

        var payload = CreateConfluentWireFormatPayload(100, "{\"hello\":\"world\"}");

        Action act = () => formatter.Format(payload, prettyPrint: true);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Schema Registry URL is not configured*");
    }

    private static byte[] CreateConfluentWireFormatPayload(int schemaId, string jsonContent)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(jsonContent);
        var buffer = new byte[5 + jsonBytes.Length];
        buffer[0] = 0x00; // Magic byte
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(1, 4), schemaId);
        Array.Copy(jsonBytes, 0, buffer, 5, jsonBytes.Length);
        return buffer;
    }
}
