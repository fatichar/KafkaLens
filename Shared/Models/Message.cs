using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KafkaLens.Shared.Models;

public class Message
{
    public long EpochMillis { get; }
    public Dictionary<string, byte[]> Headers { get; }
    public string KeyText { get; }
    public string ValueText { get; }
    public int Partition { get; set; }
    public long Offset { get; set; }

    public Message(long epochMillis, Dictionary<string, byte[]> headers, byte[]? key, byte[]? value)
    {
        EpochMillis = epochMillis;
        Headers = headers;
        Key = key;
        Value = value;
        KeyText = GetKeyText(key);
        ValueText = Value == null ? "" : Encoding.Default.GetString(Value);
    }

    private static string GetKeyText(byte[]? bytes)
    {
        if (bytes == null)
        {
            return "";
        }
        
        var stringValue = Encoding.ASCII.GetString(bytes);
        if (stringValue.ToCharArray().ToList().TrueForAll(IsAscii))
        {
            return stringValue;
        }
        var intValue = GetIntValue(bytes);
        if (intValue != 0)
        {
            return intValue.ToString();
        }

        return stringValue;
    }

    private static int GetIntValue(byte[] bytes)
    {
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        int intValue = BitConverter.ToInt32(bytes, 0);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }
        return intValue;
    }

    private static bool IsAscii(char obj)
    {
        return obj >= 32 && obj <= 126;
    }

    public byte[]? Key { get; }

    public byte[]? Value { get; }

    public void Serialize(System.IO.Stream stream)
    {
        using var writer = new System.IO.BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        writer.Write((byte)1); // Version
        writer.Write(EpochMillis);
        writer.Write(Partition);
        writer.Write(Offset);

        WriteByteArray(writer, Key);
        WriteByteArray(writer, Value);

        writer.Write(Headers.Count);
        foreach (var header in Headers)
        {
            writer.Write(header.Key);
            WriteByteArray(writer, header.Value);
        }
    }

    private static void WriteByteArray(System.IO.BinaryWriter writer, byte[]? data)
    {
        if (data == null)
        {
            writer.Write(-1);
        }
        else
        {
            writer.Write(data.Length);
            writer.Write(data);
        }
    }

    public static Message Deserialize(System.IO.Stream stream)
    {
        using var reader = new System.IO.BinaryReader(stream, System.Text.Encoding.UTF8, true);
        var version = reader.ReadByte();
        if (version != 1)
        {
            throw new System.NotSupportedException($"Message version {version} is not supported.");
        }

        var epochMillis = reader.ReadInt64();
        var partition = reader.ReadInt32();
        var offset = reader.ReadInt64();

        var key = ReadByteArray(reader);
        var value = ReadByteArray(reader);

        var headersCount = reader.ReadInt32();
        var headers = new Dictionary<string, byte[]>(headersCount);
        for (int i = 0; i < headersCount; i++)
        {
            var headerKey = reader.ReadString();
            var headerValue = ReadByteArray(reader) ?? Array.Empty<byte>();
            headers.Add(headerKey, headerValue);
        }

        var message = new Message(epochMillis, headers, key, value)
        {
            Partition = partition,
            Offset = offset
        };
        return message;
    }

    private static byte[]? ReadByteArray(System.IO.BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length == -1)
        {
            return null;
        }
        return reader.ReadBytes(length);
    }
}