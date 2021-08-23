using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KafkaLens.Shared.Models
{
    public class Message
    {
        public Message(object key, object body)
        {
            Key = key;
            Body = body;
        }

        public object Key { get; }
        public object Body { get; }
    }
}
