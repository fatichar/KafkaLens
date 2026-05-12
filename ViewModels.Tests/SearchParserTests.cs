using KafkaLens.ViewModels.Search;

namespace KafkaLens.ViewModels.Tests;

public class SearchParserTests
{
    [Theory]
    [InlineData("India", "India", true)]
    [InlineData("India", "india", true)]
    [InlineData("India", "Russia", false)]
    public void TestSingleWord(string query, string text, bool expected)
    {
        var expression = SearchParser.Parse(query);
        expression.Matches(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("\"good boy\"", "this is a good boy", true)]
    [InlineData("\"good boy\"", "good boy", true)]
    [InlineData("\"good boy\"", "good girl", false)]
    public void TestQuotedPhrase(string query, string text, bool expected)
    {
        var expression = SearchParser.Parse(query);
        expression.Matches(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("India Russia", "India", true)]
    [InlineData("India Russia", "Russia", true)]
    [InlineData("India Russia", "USA", false)]
    [InlineData("\"good boy\" \"bad girl\"", "good boy", true)]
    [InlineData("\"good boy\" \"bad girl\"", "bad girl", true)]
    [InlineData("\"good boy\" \"bad girl\"", "good girl", false)]
    public void TestImplicitOr(string query, string text, bool expected)
    {
        var expression = SearchParser.Parse(query);
        expression.Matches(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("India || Russia", "India", true)]
    [InlineData("India || Russia", "Russia", true)]
    [InlineData("India || Russia", "USA", false)]
    public void TestExplicitOr(string query, string text, bool expected)
    {
        var expression = SearchParser.Parse(query);
        expression.Matches(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("India && Russia", "India Russia", true)]
    [InlineData("India && Russia", "India", false)]
    [InlineData("India && Russia", "Russia", false)]
    public void TestExplicitAnd(string query, string text, bool expected)
    {
        var expression = SearchParser.Parse(query);
        expression.Matches(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("!India", "Russia", true)]
    [InlineData("!India", "India", false)]
    public void TestNot(string query, string text, bool expected)
    {
        var expression = SearchParser.Parse(query);
        expression.Matches(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("A B && C", "A", true)]
    [InlineData("A B && C", "B C", true)]
    [InlineData("A B && C", "B", false)]
    [InlineData("A && B C", "A B", true)]
    [InlineData("A && B C", "C", true)]
    [InlineData("A && B C", "A", false)]
    public void TestPrecedence(string query, string text, bool expected)
    {
        var expression = SearchParser.Parse(query);
        expression.Matches(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("(A || B) && C", "A C", true)]
    [InlineData("(A || B) && C", "B C", true)]
    [InlineData("(A || B) && C", "A", false)]
    [InlineData("(A || B) && C", "C", false)]
    public void TestParentheses(string query, string text, bool expected)
    {
        var expression = SearchParser.Parse(query);
        expression.Matches(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("!A && B", "B", true)]
    [InlineData("!A && B", "A B", false)]
    [InlineData("!(A && B)", "A", true)]
    [InlineData("!(A && B)", "B", true)]
    [InlineData("!(A && B)", "A B", false)]
    public void TestNotPrecedence(string query, string text, bool expected)
    {
        var expression = SearchParser.Parse(query);
        expression.Matches(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("A & B", "A", true)]
    [InlineData("A & B", "&", true)]
    [InlineData("A & B", "B", true)]
    [InlineData("|", "|", true)]
    [InlineData("A & B", "C", false)]
    public void TestInvalidOperatorsAsTerms(string query, string text, bool expected)
    {
        var expression = SearchParser.Parse(query);
        expression.Matches(text).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "anything", true)]
    [InlineData("", "anything", true)]
    [InlineData("   ", "anything", true)]
    public void TestEmptyQuery(string query, string text, bool expected)
    {
        var expression = SearchParser.Parse(query);
        expression.Matches(text).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, false, "anything", false)]
    [InlineData("", false, "anything", false)]
    [InlineData("   ", false, "anything", false)]
    [InlineData(null, true, "anything", true)]
    public void TestDefaultMatch(string query, bool defaultMatch, string text, bool expected)
    {
        var expression = SearchParser.Parse(query, defaultMatch);
        expression.Matches(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("!!India", "India", true)]
    [InlineData("!!!India", "India", false)]
    [InlineData("!!!India", "Russia", true)]
    public void TestMultipleNot(string query, string text, bool expected)
    {
        var expression = SearchParser.Parse(query);
        expression.Matches(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("(India", "India", true)]
    [InlineData("(India", "Russia", false)]
    [InlineData("India)", "India", true)]
    [InlineData("India)", "Russia", false)]
    [InlineData("((India", "India", true)]
    public void TestUnmatchedParentheses(string query, string text, bool expected)
    {
        var expression = SearchParser.Parse(query);
        expression.Matches(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("()", "anything", true)]
    [InlineData("() India", "India", true)]
    public void TestEmptyParentheses(string query, string text, bool expected)
    {
        var expression = SearchParser.Parse(query);
        expression.Matches(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("\"India", "India", true)]
    [InlineData("\"India", "Russia", false)]
    [InlineData("\"India Russia", "India Russia", true)]
    public void TestUnterminatedQuote(string query, string text, bool expected)
    {
        var expression = SearchParser.Parse(query);
        expression.Matches(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("  India   &&   Russia  ", "India Russia", true)]
    [InlineData("India||Russia", "India", true)]
    [InlineData("India&&Russia", "India Russia", true)]
    public void TestWhitespace(string query, string text, bool expected)
    {
        var expression = SearchParser.Parse(query);
        expression.Matches(text).Should().Be(expected);
    }
}
