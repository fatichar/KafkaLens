using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KafkaLens.Formatting;

public class JsonFormatter : IMessageFormatter
{
    const char INDENT_CHAR = ' ';
    const int INDENT_SIZE = 4;

    public string? Format(byte[] data, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return Format(data, true);
        }
        var text = Encoding.UTF8.GetString(data);
        if (text.Length < data.Length)
        {
            return null;
        }
        searchText = searchText.ToLowerInvariant();

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

                    Filter(jObject, searchText);
                    
                    jObject.WriteTo(jw);
                }
            }

            stringWriter.Close();
            var formatted = stringWriter.ToString();
            return formatted;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Filter(JObject jObject, string searchText)
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
                Filter(jObj, searchText);
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
            tokens.Add(pair.Key, pair.Value);
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
                Filter(jObj, searchText);
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

    private static bool Matches(object val, string searchText)
    {
        return val.ToString().ToLower().Contains(searchText);
    }

    public string? Format(byte[] data, bool prettyPrint)
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
                    jw.Formatting = prettyPrint ? Newtonsoft.Json.Formatting.Indented
                            : Newtonsoft.Json.Formatting.None;
                    jw.IndentChar = INDENT_CHAR;
                    jw.Indentation = INDENT_SIZE;

                    jObject.WriteTo(jw);
                }
            }

            stringWriter.Close();
            var formatted = stringWriter.ToString();
            return formatted;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string Name => "Json";
    public bool AtomicArrays { get; set; }
}