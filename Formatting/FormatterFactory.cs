using System.Reflection;
using Serilog;

namespace KafkaLens.Formatting;

public class FormatterFactory
{
    public static FormatterFactory Instance { get; }
    private readonly IDictionary<string, IMessageFormatter> formatters = new Dictionary<string, IMessageFormatter>();

    static FormatterFactory()
    {
        Instance = new FormatterFactory();
    }

    private FormatterFactory()
    {
        formatters.Add("Json", new JsonFormatter());
        formatters.Add("Text", new TextFormatter());
    }

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

    private void AddFormatter(IMessageFormatter formatter)
    {
        formatters.Add(formatter.Name, formatter);
    }

    public void RemoveFormatter(string name)
    {
        formatters.Remove(name);
    }

    public IEnumerable<string> GetFormatterNames()
    {
        return formatters.Keys.ToList();
    }

    public List<IMessageFormatter> GetFormatters()
    {
        return formatters.Values.ToList();
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
            var formatter = Activator.CreateInstance(formatterType) as IMessageFormatter;
            if (formatter != null)
            {
                loadedFormatters.Add(formatter);
            }
        }

        return loadedFormatters;
    }
}