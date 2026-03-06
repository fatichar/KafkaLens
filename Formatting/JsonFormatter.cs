using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KafkaLens.Formatting;

public class JsonFormatter : IMessageFormatter
{
    private const char INDENT_CHAR = ' ';
    private const int INDENT_SIZE = 4;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public string? Format(byte[] data, string searchText, bool useObjectFilter = true)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return Format(data, true);
        }
        if (!TryDecodeUtf8(data, out var text))
        {
            return null;
        }

        try
        {
            if (!useObjectFilter)
            {
                return FilterLines(text, searchText);
            }

            var jObject = JObject.Parse(text);

            using var sw = new StringWriter();
            using (var jw = new JsonTextWriter(sw))
            {
                jw.Formatting = Newtonsoft.Json.Formatting.Indented;
                jw.IndentChar = INDENT_CHAR;
                jw.Indentation = INDENT_SIZE;

                FilterObjects(jObject, searchText);

                jObject.WriteTo(jw);
            }

            return sw.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string? FilterLines(string text, string searchText)
    {
        try
        {
            var jObject = JObject.Parse(text);
            var formatted = jObject.ToString(Newtonsoft.Json.Formatting.Indented);
            // split on both CRLF and LF to be robust across platforms
            var lines = formatted.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            var filteredLines = lines.Where(line =>
                !string.IsNullOrEmpty(line) && line.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);
            return string.Join(Environment.NewLine, filteredLines);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void FilterObjects(JObject jObject, string searchText)
    {
        var tokens = GetTokens(jObject);

        foreach (var pair in tokens)
        {
            if (Matches(pair.Key, searchText))
            {
                continue;
            }

            if (pair.Value is JValue val)
            {
                if (!Matches(val, searchText))
                {
                    jObject.Remove(pair.Key);
                }
            }
            else if (pair.Value is JObject jObj)
            {
                FilterObjects(jObj, searchText);
                if (jObj.Count == 0)
                {
                    jObject.Remove(pair.Key);
                }
            }
            else if (pair.Value is JArray jArr)
            {
                if (AtomicArrays)
                {
                    FilterAtomic(jArr, searchText);
                }
                else
                {
                    Filter(jArr, searchText);
                }
                if (jArr.Count == 0)
                {
                    jObject.Remove(pair.Key);
                }
            }
        }
    }

    private void FilterAtomic(JArray jArr, string searchText)
    {
        // if any element matches, keep the whole array, else remove every item
        var tokens = new List<JToken>(jArr.Children());
        bool keep = false;
        foreach (var token in tokens)
        {
            if (Matches(token, searchText))
            {
                keep = true;
                break;
            }
        }
        if (!keep)
        {
            jArr.RemoveAll();
        }
    }

    private static Dictionary<string, JToken> GetTokens(JObject jObject)
    {
        var tokens = new Dictionary<string, JToken>();
        foreach (var pair in jObject)
        {
            tokens.Add(pair.Key, pair.Value!);
        }

        return tokens;
    }

    private void Filter(JArray jArray, string searchText)
    {
        var tokens = new List<JToken>(jArray.Children());
        foreach (var item in tokens)
        {
            if (item is JObject jObj)
            {
                FilterObjects(jObj, searchText);
                if (jObj.Count == 0)
                {
                    jArray.Remove(item);
                }
            }
            else if (item is JArray jArr)
            {
                Filter(jArr, searchText);
            }
            else if (item is JValue jVal)
            {
                if (jVal.Value == null || !Matches(jVal.Value, searchText))
                {
                    jArray.Remove(item);
                }
            }
        }
    }

    private static bool Matches(object? val, string searchText)
    {
        if (val == null) return false;
        var s = val.ToString();
        return !string.IsNullOrEmpty(s) && s.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public string? Format(byte[] data, bool prettyPrint)
    {
        if (!TryDecodeUtf8(data, out var text))
        {
            return null;
        }

        try
        {
            var jObject = JObject.Parse(text);

            using var sw = new StringWriter();
            using (var jw = new JsonTextWriter(sw))
            {
                jw.Formatting = prettyPrint ? Newtonsoft.Json.Formatting.Indented
                    : Newtonsoft.Json.Formatting.None;
                jw.IndentChar = INDENT_CHAR;
                jw.Indentation = INDENT_SIZE;

                jObject.WriteTo(jw);
            }

            return sw.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryDecodeUtf8(byte[] data, out string text)
    {
        try
        {
            text = StrictUtf8.GetString(data);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    public string Name => "Json";
    public bool AtomicArrays { get; set; }
}
