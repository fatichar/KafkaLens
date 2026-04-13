using System.Reflection;
using Serilog;

namespace KafkaLens.Formatting;

public class FormatterFactory
{
    public static FormatterFactory Instance { get; }
    private readonly IDictionary<string, IMessageFormatter> formatters = new Dictionary<string, IMessageFormatter>();
    private readonly List<string> builtInFormatterNames = new();
    private readonly HashSet<string> builtInFormatterNamesSet = new(StringComparer.Ordinal);
    private readonly List<string> pluginFormatterNames = new();
    private readonly HashSet<string> pluginFormatterNamesSet = new(StringComparer.Ordinal);
    private readonly List<string> builtInKeyFormatterNames = new();
    private readonly HashSet<string> builtInKeyFormatterNamesSet = new(StringComparer.Ordinal);
    private const string JSON = "Json";
    private const string TEXT = "Text";
    private const string INT8 = "Int8";
    private const string UINT8 = "UInt8";
    private const string INT16 = "Int16";
    private const string UINT16 = "UInt16";
    private const string INT32 = "Int32";
    private const string UINT32 = "UInt32";
    private const string INT64 = "Int64";
    private const string UINT64 = "UInt64";

    static FormatterFactory()
    {
        Instance = new FormatterFactory();
    }

    private FormatterFactory()
    {
        AddBuiltInFormatter(new JsonFormatter(), supportsKeyFormatting: true);
        AddBuiltInFormatter(new TextFormatter(), supportsKeyFormatting: true);
        AddBuiltInFormatter(new Int8Formatter(), supportsKeyFormatting: true);
        AddBuiltInFormatter(new UInt8Formatter(), supportsKeyFormatting: true);
        AddBuiltInFormatter(new Int16Formatter(), supportsKeyFormatting: true);
        AddBuiltInFormatter(new UInt16Formatter(), supportsKeyFormatting: true);
        AddBuiltInFormatter(new Int32Formatter(), supportsKeyFormatting: true);
        AddBuiltInFormatter(new UInt32Formatter(), supportsKeyFormatting: true);
        AddBuiltInFormatter(new Int64Formatter(), supportsKeyFormatting: true);
        AddBuiltInFormatter(new UInt64Formatter(), supportsKeyFormatting: true);
    }

    public IMessageFormatter DefaultFormatter => formatters[JSON];

    public static void AddFromPath(string pluginsPath)
    {
        var formattersPath = Path.Combine(pluginsPath, "Formatters");
        var formattersDir = Directory.CreateDirectory(formattersPath);
        Instance.AddFormatters(formattersDir);
    }

    public IMessageFormatter GetFormatter(string name)
    {
        return formatters[name];
    }

    public void AddFormatter(IMessageFormatter formatter)
    {
        formatters.Add(formatter.Name, formatter);
        if (!builtInFormatterNamesSet.Contains(formatter.Name) && pluginFormatterNamesSet.Add(formatter.Name))
        {
            pluginFormatterNames.Add(formatter.Name);
        }
    }

    public void RemoveFormatter(string name)
    {
        formatters.Remove(name);
        if (pluginFormatterNamesSet.Remove(name))
        {
            pluginFormatterNames.Remove(name);
        }
    }

    public IEnumerable<string> GetFormatterNames()
    {
        return formatters.Keys.ToList();
    }

    public IList<string> GetBuiltInKeyFormatterNames()
    {
        return builtInKeyFormatterNames.ToList();
    }

    public IList<string> GetBuiltInFormatterNames()
    {
        return builtInFormatterNames.ToList();
    }

    public IList<string> GetPluginFormatterNames()
    {
        return pluginFormatterNames.ToList();
    }

    public List<IMessageFormatter> GetFormatters()
    {
        return formatters.Values.ToList();
    }

    private void AddBuiltInFormatter(IMessageFormatter formatter, bool supportsKeyFormatting)
    {
        formatters.Add(formatter.Name, formatter);
        if (builtInFormatterNamesSet.Add(formatter.Name))
        {
            builtInFormatterNames.Add(formatter.Name);
        }
        if (supportsKeyFormatting && builtInKeyFormatterNamesSet.Add(formatter.Name))
        {
            builtInKeyFormatterNames.Add(formatter.Name);
        }
    }

    private void AddFormatters(DirectoryInfo formattersDir)
    {
        foreach (var file in formattersDir.GetFiles("*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(file.FullName);
                var newFormatters = CreateFormatters(assembly);
                foreach (var formatter in newFormatters)
                {
                    AddFormatter(formatter);
                }
            }
            catch (FileNotFoundException e)
            {
                Log.Error(e, "Could not load {File}", file.FullName);
                throw;
            }
            catch (FileLoadException e)
            {
                Log.Error(e, "Could not load {File}", file.FullName);
                throw;
            }
            catch (Exception e)
            {
                Log.Error(e, "Could not load {File}", file.FullName);
            }
        }
    }

    private List<IMessageFormatter> CreateFormatters(Assembly assembly)
    {
        var types = assembly.GetExportedTypes();
        var formatterTypes = types.Where(t => t.GetInterfaces().Contains(typeof(IMessageFormatter)));
        var loadedFormatters = new List<IMessageFormatter>();
        foreach (var formatterType in formatterTypes)
        {
            if (Activator.CreateInstance(formatterType) is IMessageFormatter formatter)
            {
                loadedFormatters.Add(formatter);
            }
        }

        return loadedFormatters;
    }
}
