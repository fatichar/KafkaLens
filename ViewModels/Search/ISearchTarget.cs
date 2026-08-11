namespace KafkaLens.ViewModels.Search;

/// <summary>
/// A value that a filter expression can be evaluated against. Implementers decide which
/// fields a search term is allowed to match, so a filter can span body, key, headers and
/// topic without the expression tree knowing about message structure.
/// </summary>
public interface ISearchTarget
{
    /// <summary>True when any searchable field contains <paramref name="term"/>, ignoring case.</summary>
    bool ContainsTerm(string term);
}

/// <summary>Searches a single string. Used where a filter is applied to plain text.</summary>
public sealed class TextSearchTarget(string text) : ISearchTarget
{
    public bool ContainsTerm(string term) => text.Contains(term, StringComparison.OrdinalIgnoreCase);
}
