namespace KafkaLens.ViewModels.Search;

public interface IFilterExpression
{
    bool Matches(string text);
}
