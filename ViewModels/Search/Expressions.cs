namespace KafkaLens.ViewModels.Search;

public class TermExpression(string term) : IFilterExpression
{
    public bool Matches(string text)
    {
        return text.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}

public class AndExpression(IFilterExpression left, IFilterExpression right) : IFilterExpression
{
    public bool Matches(string text)
    {
        return left.Matches(text) && right.Matches(text);
    }
}

public class OrExpression(IFilterExpression left, IFilterExpression right) : IFilterExpression
{
    public bool Matches(string text)
    {
        return left.Matches(text) || right.Matches(text);
    }
}

public class NotExpression(IFilterExpression expression) : IFilterExpression
{
    public bool Matches(string text)
    {
        return !expression.Matches(text);
    }
}

public class AllMatchExpression : IFilterExpression
{
    public bool Matches(string text) => true;
}

public class NoneMatchExpression : IFilterExpression
{
    public bool Matches(string text) => false;
}
