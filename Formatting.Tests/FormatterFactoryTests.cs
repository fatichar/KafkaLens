using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace KafkaLens.Formatting.Tests;

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
    public void GetFormatter_Int32_ReturnsInt32Formatter()
    {
        var formatter = factory.GetFormatter("Int32");

        Assert.NotNull(formatter);
        Assert.IsType<Int32Formatter>(formatter);
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
        Assert.Contains("Int32", names);
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

    [Fact]
    public void GetBuiltInKeyFormatterNames_ContainsNumericAndText()
    {
        var names = factory.GetBuiltInKeyFormatterNames();

        Assert.Contains("Text", names);
        Assert.Contains("Int8", names);
        Assert.Contains("UInt8", names);
        Assert.Contains("Int16", names);
        Assert.Contains("UInt16", names);
        Assert.Contains("Int32", names);
        Assert.Contains("UInt32", names);
        Assert.Contains("Int64", names);
        Assert.Contains("UInt64", names);
        Assert.DoesNotContain("Json", names);
    }
}