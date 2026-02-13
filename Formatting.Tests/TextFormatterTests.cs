using System;
using System.Text;
using Xunit;

namespace KafkaLens.Formatting;

public class TextFormatterTests
{
    private readonly TextFormatter formatter = new();

    #region Format(byte[], bool)

    [Fact]
    public void Format_ReturnsUtf8String()
    {
        var text = "Hello, World!";
        var data = Encoding.UTF8.GetBytes(text);

        var result = formatter.Format(data, true);

        Assert.Equal(text, result);
    }

    [Fact]
    public void Format_EmptyData_ReturnsEmptyString()
    {
        var data = Array.Empty<byte>();

        var result = formatter.Format(data, true);

        Assert.Equal("", result);
    }

    [Fact]
    public void Format_PrettyPrintFlag_DoesNotAffectOutput()
    {
        var text = "some text";
        var data = Encoding.UTF8.GetBytes(text);

        var prettyResult = formatter.Format(data, true);
        var compactResult = formatter.Format(data, false);

        Assert.Equal(prettyResult, compactResult);
    }

    [Fact]
    public void Format_MultilineText_PreservesNewlines()
    {
        var text = "line1\nline2\nline3";
        var data = Encoding.UTF8.GetBytes(text);

        var result = formatter.Format(data, true);

        Assert.Equal(text, result);
    }

    #endregion

    #region Format(byte[], string, bool) - search

    [Fact]
    public void Format_WithNullSearchText_ReturnsFullText()
    {
        var text = "Hello, World!";
        var data = Encoding.UTF8.GetBytes(text);

        var result = formatter.Format(data, null!, false);

        Assert.Equal(text, result);
    }

    [Fact]
    public void Format_WithEmptySearchText_ReturnsFullText()
    {
        var text = "Hello, World!";
        var data = Encoding.UTF8.GetBytes(text);

        var result = formatter.Format(data, "", false);

        Assert.Equal(text, result);
    }

    [Fact]
    public void Format_WithMatchingSearchText_ReturnsText()
    {
        var text = "Hello, World!";
        var data = Encoding.UTF8.GetBytes(text);

        var result = formatter.Format(data, "hello", false);

        Assert.Equal(text, result);
    }

    [Fact]
    public void Format_WithNonMatchingSearchText_ReturnsEmpty()
    {
        var text = "Hello, World!";
        var data = Encoding.UTF8.GetBytes(text);

        var result = formatter.Format(data, "xyz", false);

        Assert.Equal("", result);
    }

    [Fact]
    public void Format_SearchIsCaseInsensitive()
    {
        var text = "Hello, World!";
        var data = Encoding.UTF8.GetBytes(text);

        var result = formatter.Format(data, "HELLO", false);

        Assert.Equal(text, result);
    }

    #endregion

    #region Name

    [Fact]
    public void Name_ReturnsText()
    {
        Assert.Equal("Text", formatter.Name);
    }

    #endregion
}
