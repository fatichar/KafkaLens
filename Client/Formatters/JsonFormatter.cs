using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KafkaLens.Client.Formatters
{
    public class JsonFormatter : IMessageFormatter
    {
        public string DisplayName { get; set; } = "Json";
        JsonWriterOptions Options = new() { Indented = true, };

        public string Format(string jsonString)
        {
            using (JsonDocument document = JsonDocument.Parse(jsonString))
            {
                MemoryStream stream = new();
                Utf8JsonWriter writer = new(stream, Options);
                document.WriteTo(writer);
                writer.Flush();

                string json = Encoding.UTF8.GetString(stream.ToArray());
                return json;
            }
        }
    }
}
