namespace KafkaLens.ViewModels.Search;

public class TermExpression(string term) : IFilterExpression
{
    public bool Matches(ISearchTarget target)
    {
        return target.ContainsTerm(term);
    }
}

public class AndExpression(IFilterExpression left, IFilterExpression right) : IFilterExpression
{
    public bool Matches(ISearchTarget target)
    {
        return left.Matches(target) && right.Matches(target);
    }
}

public class OrExpression(IFilterExpression left, IFilterExpression right) : IFilterExpression
{
    public bool Matches(ISearchTarget target)
    {
        return left.Matches(target) || right.Matches(target);
    }
}

public class NotExpression(IFilterExpression expression) : IFilterExpression
{
    public bool Matches(ISearchTarget target)
    {
        return !expression.Matches(target);
    }
}

public class AllMatchExpression : IFilterExpression
{
    public bool Matches(ISearchTarget target) => true;
}

public class NoneMatchExpression : IFilterExpression
{
    public bool Matches(ISearchTarget target) => false;
}
