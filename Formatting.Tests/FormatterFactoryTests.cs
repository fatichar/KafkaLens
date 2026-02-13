using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace KafkaLens.Formatting;

public class FormatterFactoryTests
{
    private readonly FormatterFactory factory = FormatterFactory.Instance;

    [Fact]
    public void Instance_IsNotNull()
    {
        Assert.NotNull(FormatterFactory.Instance);
    }

    [Fact]
    public void Instance_IsSingleton()
    {
        var instance1 = FormatterFactory.Instance;
        var instance2 = FormatterFactory.Instance;

        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void DefaultFormatter_IsJsonFormatter()
    {
        var defaultFormatter = factory.DefaultFormatter;

        Assert.NotNull(defaultFormatter);
        Assert.Equal("Json", defaultFormatter.Name);
        Assert.IsType<JsonFormatter>(defaultFormatter);
    }

    [Fact]
    public void GetFormatter_Json_ReturnsJsonFormatter()
    {
        var formatter = factory.GetFormatter("Json");

        Assert.NotNull(formatter);
        Assert.IsType<JsonFormatter>(formatter);
    }

    [Fact]
    public void GetFormatter_Text_ReturnsTextFormatter()
    {
        var formatter = factory.GetFormatter("Text");

        Assert.NotNull(formatter);
        Assert.IsType<TextFormatter>(formatter);
    }

    [Fact]
    public void GetFormatter_UnknownName_ThrowsKeyNotFoundException()
    {
        Assert.Throws<KeyNotFoundException>(() => factory.GetFormatter("Unknown"));
    }

    [Fact]
    public void GetFormatterNames_ContainsJsonAndText()
    {
        var names = factory.GetFormatterNames().ToList();

        Assert.Contains("Json", names);
        Assert.Contains("Text", names);
    }

    [Fact]
    public void GetFormatters_ContainsAtLeastTwoFormatters()
    {
        var formatters = factory.GetFormatters();

        Assert.True(formatters.Count >= 2);
    }

    [Fact]
    public void GetFormatters_AllHaveNames()
    {
        var formatters = factory.GetFormatters();

        Assert.All(formatters, f => Assert.False(string.IsNullOrEmpty(f.Name)));
    }
}
