namespace KafkaLens.ViewModels.Search;

public interface IFilterExpression
{
    bool Matches(ISearchTarget target);

    /// <summary>Convenience overload for matching against plain text.</summary>
    bool Matches(string text) => Matches(new TextSearchTarget(text));
}
