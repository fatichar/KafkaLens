using System.Text;
using Xunit;

namespace KafkaLens.Formatting;

public class JsonFormatterTests
{
    private readonly JsonFormatter formatter = new();

    #region Format(byte[], bool)

    [Fact]
    public void Format_ValidJson_PrettyPrint_ReturnsIndentedJson()
    {
        var json = """{"name":"Alice","age":30}""";
        var data = Encoding.UTF8.GetBytes(json);

        var result = formatter.Format(data, true);

        Assert.NotNull(result);
        Assert.Contains("\"name\"", result);
        Assert.Contains("\n", result);
    }

    [Fact]
    public void Format_ValidJson_NoPrettyPrint_ReturnsCompactJson()
    {
        var json = """{"name":"Alice","age":30}""";
        var data = Encoding.UTF8.GetBytes(json);

        var result = formatter.Format(data, false);

        Assert.NotNull(result);
        Assert.DoesNotContain("\n", result);
    }

    [Fact]
    public void Format_InvalidJson_ReturnsNull()
    {
        var data = Encoding.UTF8.GetBytes("not json at all");

        var result = formatter.Format(data, true);

        Assert.Null(result);
    }

    [Fact]
    public void Format_EmptyObject_ReturnsEmptyObject()
    {
        var data = Encoding.UTF8.GetBytes("{}");

        var result = formatter.Format(data, true);

        Assert.NotNull(result);
        Assert.Equal("{}", result);
    }

    [Fact]
    public void Format_NestedJson_PrettyPrint_ReturnsIndented()
    {
        var json = """{"person":{"name":"Bob","address":{"city":"NYC"}}}""";
        var data = Encoding.UTF8.GetBytes(json);

        var result = formatter.Format(data, true);

        Assert.NotNull(result);
        Assert.Contains("person", result);
        Assert.Contains("address", result);
        Assert.Contains("NYC", result);
    }

    #endregion

    #region Format(byte[], string, bool) - search/filter

    [Fact]
    public void Format_WithEmptySearchText_ReturnsPrettyPrintedJson()
    {
        var json = """{"name":"Alice","age":30}""";
        var data = Encoding.UTF8.GetBytes(json);

        var result = formatter.Format(data, "", true);

        Assert.NotNull(result);
        Assert.Contains("Alice", result);
    }

    [Fact]
    public void Format_WithWhitespaceSearchText_ReturnsPrettyPrintedJson()
    {
        var json = """{"name":"Alice","age":30}""";
        var data = Encoding.UTF8.GetBytes(json);

        var result = formatter.Format(data, "   ", true);

        Assert.NotNull(result);
    }

    [Fact]
    public void Format_WithSearchText_ObjectFilter_FiltersMatchingProperties()
    {
        var json = """{"name":"Alice","city":"NYC","age":30}""";
        var data = Encoding.UTF8.GetBytes(json);

        var result = formatter.Format(data, "alice", true);

        Assert.NotNull(result);
        Assert.Contains("Alice", result);
    }

    [Fact]
    public void Format_WithSearchText_ObjectFilter_RemovesNonMatchingProperties()
    {
        var json = """{"name":"Alice","city":"NYC","age":30}""";
        var data = Encoding.UTF8.GetBytes(json);

        var result = formatter.Format(data, "alice", true);

        Assert.NotNull(result);
        Assert.DoesNotContain("city", result);
        Assert.DoesNotContain("age", result);
    }

    [Fact]
    public void Format_WithSearchText_MatchesKeyName()
    {
        var json = """{"name":"Alice","city":"NYC"}""";
        var data = Encoding.UTF8.GetBytes(json);

        var result = formatter.Format(data, "name", true);

        Assert.NotNull(result);
        Assert.Contains("name", result);
        Assert.Contains("Alice", result);
    }

    [Fact]
    public void Format_WithSearchText_LineFilter_FiltersLines()
    {
        var json = """{"name":"Alice","city":"NYC","age":30}""";
        var data = Encoding.UTF8.GetBytes(json);

        var result = formatter.Format(data, "alice", useObjectFilter: false);

        Assert.NotNull(result);
        Assert.Contains("Alice", result);
        Assert.DoesNotContain("city", result);
    }

    [Fact]
    public void Format_WithSearchText_InvalidJson_ReturnsNull()
    {
        var data = Encoding.UTF8.GetBytes("not json");

        var result = formatter.Format(data, "search", true);

        Assert.Null(result);
    }

    [Fact]
    public void Format_WithSearchText_CaseInsensitive()
    {
        var json = """{"Name":"ALICE"}""";
        var data = Encoding.UTF8.GetBytes(json);

        var result = formatter.Format(data, "alice", true);

        Assert.NotNull(result);
        Assert.Contains("ALICE", result);
    }

    [Fact]
    public void Format_NestedObject_FilterRemovesEmptyBranches()
    {
        var json = """{"person":{"name":"Alice"},"location":{"city":"NYC"}}""";
        var data = Encoding.UTF8.GetBytes(json);

        var result = formatter.Format(data, "alice", true);

        Assert.NotNull(result);
        Assert.Contains("Alice", result);
        Assert.DoesNotContain("location", result);
        Assert.DoesNotContain("NYC", result);
    }

    [Fact]
    public void Format_ArrayFilter_RemovesNonMatchingElements()
    {
        var json = """{"items":["apple","banana","cherry"]}""";
        var data = Encoding.UTF8.GetBytes(json);

        var result = formatter.Format(data, "apple", true);

        Assert.NotNull(result);
        Assert.Contains("apple", result);
        Assert.DoesNotContain("banana", result);
    }

    [Fact]
    public void Format_AtomicArrayFilter_KeepsWholeArrayIfAnyMatch()
    {
        formatter.AtomicArrays = true;
        var json = """{"items":["apple","banana","cherry"]}""";
        var data = Encoding.UTF8.GetBytes(json);

        var result = formatter.Format(data, "apple", true);

        Assert.NotNull(result);
        Assert.Contains("apple", result);
        Assert.Contains("banana", result);
        Assert.Contains("cherry", result);
    }

    [Fact]
    public void Format_AtomicArrayFilter_RemovesArrayIfNoMatch()
    {
        formatter.AtomicArrays = true;
        var json = """{"items":["apple","banana"],"count":5}""";
        var data = Encoding.UTF8.GetBytes(json);

        var result = formatter.Format(data, "count", true);

        Assert.NotNull(result);
        Assert.DoesNotContain("apple", result);
    }

    #endregion

    #region Name

    [Fact]
    public void Name_ReturnsJson()
    {
        Assert.Equal("Json", formatter.Name);
    }

    #endregion
}
