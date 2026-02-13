using System;

namespace KafkaLens.ViewModels.Search;

public class TermExpression : IFilterExpression
{
    private readonly string term;

    public TermExpression(string term)
    {
        this.term = term;
    }

    public bool Matches(string text)
    {
        return text.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}

public class AndExpression : IFilterExpression
{
    private readonly IFilterExpression left;
    private readonly IFilterExpression right;

    public AndExpression(IFilterExpression left, IFilterExpression right)
    {
        this.left = left;
        this.right = right;
    }

    public bool Matches(string text)
    {
        return left.Matches(text) && right.Matches(text);
    }
}

public class OrExpression : IFilterExpression
{
    private readonly IFilterExpression left;
    private readonly IFilterExpression right;

    public OrExpression(IFilterExpression left, IFilterExpression right)
    {
        this.left = left;
        this.right = right;
    }

    public bool Matches(string text)
    {
        return left.Matches(text) || right.Matches(text);
    }
}

public class NotExpression : IFilterExpression
{
    private readonly IFilterExpression expression;

    public NotExpression(IFilterExpression expression)
    {
        this.expression = expression;
    }

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
