using System.Collections.Generic;
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
        KeyText = key == null ? "" : Encoding.Default.GetString(key);
        ValueText = this.Value == null ? "" : Encoding.Default.GetString(this.Value);
    }
    
    public byte[]? Key { get; }

    public byte[]? Value { get; }
}