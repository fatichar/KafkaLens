namespace KafkaLens.Shared.Plugins;

/// <summary>
/// Extension point: custom message deserializer.
/// Implement and annotate with <c>[KafkaLensExtension(typeof(IMessageDeserializer))]</c>.
/// </summary>
public interface IMessageDeserializer
{
    /// <summary>Name shown in the UI.</summary>
    string Name { get; }

    /// <summary>
    /// Deserializes raw bytes to a human-readable string.
    /// Returns <c>null</c> if this deserializer cannot handle the payload.
    /// </summary>
    string? Deserialize(byte[] bytes);
}
