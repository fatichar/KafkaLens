using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KafkaLens.Shared.Models
{
    public class Message
    {

        public Message(long epochMillis, Dictionary<string, byte[]> headers, byte[] key, byte[] value)
        {
            EpochMillis = epochMillis;
            Headers = headers;
            Key = key;
            Value = value;
        }

        public long EpochMillis { get; }
        public Dictionary<string, byte[]> Headers { get; }
        public byte[] Key { get; }
        public byte[] Value { get; }
        public int Partition { get; set; }
        public long Offset { get; set; }
    }
}
