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
}