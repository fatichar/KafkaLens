using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Text;

namespace KafkaLens.Formatting;

public class JsonFormatter : IMessageFormatter
{
    const char INDENT_CHAR = ' ';
    const int INDENT_SIZE = 4;

    public string? Format(byte[] data)
    {
        return FormatUsingNewtonSoft(data);
    }

    private static string? FormatUsingNewtonSoft(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);
        if (text.Length < data.Length)
        {
            return null;
        }
        try
        {
            var jObject = JObject.Parse(text);
            StringWriter stringWriter = new StringWriter();
            using (StringWriter sw = stringWriter)
            {
                using (JsonTextWriter jw = new JsonTextWriter(sw))
                {
                    jw.Formatting = Newtonsoft.Json.Formatting.Indented;
                    jw.IndentChar = INDENT_CHAR;
                    jw.Indentation = INDENT_SIZE;

                    jObject.WriteTo(jw);
                }
            }
            stringWriter.Close();
            var formatted = stringWriter.ToString();
            return formatted;
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    public string Name => "Json";
}