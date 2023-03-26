namespace KafkaLens.Formatting;

public class FormatterFactory
{
    public static FormatterFactory Instance { get; private set; }
    private readonly IDictionary<string, IMessageFormatter> formatters = new Dictionary<string, IMessageFormatter>();
    
    public FormatterFactory()
    {
        formatters.Add("Json", new JsonFormatter());
        formatters.Add("Text", new TextFormatter());
        
        Instance = this;
    }
    
    public IMessageFormatter GetFormatter(string name)
    {
        return formatters[name];
    }
    
    public void AddFormatter(IMessageFormatter formatter)
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
}