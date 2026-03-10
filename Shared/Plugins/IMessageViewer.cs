namespace KafkaLens.Shared.Plugins;

/// <summary>
/// Extension point: custom message viewer panel.
/// Implement and annotate with <c>[KafkaLensExtension(typeof(IMessageViewer))]</c>.
/// </summary>
public interface IMessageViewer
{
    /// <summary>Name shown in the viewer tab/picker.</summary>
    string Name { get; }

    /// <summary>
    /// Renders <paramref name="payload"/> to a display string.
    /// Returns <c>null</c> if this viewer cannot handle the payload.
    /// </summary>
    string? Render(string payload);
}
