using System.Collections.Generic;
using System.Text;

namespace KafkaLens.Shared.Models
{
    public class Message
    {
        private byte[] key;
        private byte[] value;

        public Message(long epochMillis, Dictionary<string, byte[]> headers, byte[] key, byte[] value)
        {
            EpochMillis = epochMillis;
            Headers = headers;
            Key = key;
            Value = value;
        }

        public long EpochMillis { get; }
        public Dictionary<string, byte[]> Headers { get; }
        public byte[] Key
        {
            get => key;
            set
            {
                key = value;
                KeyText = Encoding.Default.GetString(key);
            }
        }
        public byte[] Value
        {
            get => value; 
            set
            {
                this.value = value;
                ValueText = Encoding.Default.GetString(value);
            }
        }
        public string KeyText { get; set; }
        public string ValueText { get; set; }
        public int Partition { get; set; }
        public long Offset { get; set; }
    }
}
