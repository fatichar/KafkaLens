using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KafkaLens.Client.Formatters;

public interface IMessageFormatter
{
    string DisplayName { get; set; }

    string Format(string data);
}