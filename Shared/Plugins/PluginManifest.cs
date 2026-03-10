using System.Text.Json.Serialization;

namespace KafkaLens.Shared.Plugins;

/// <summary>
/// Represents the optional plugin.json manifest file found inside a plugin folder.
/// Values here supplement or override the assembly-level <see cref="KafkaLensPluginAttribute"/>.
/// </summary>
public sealed class PluginManifest
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("author")]
    public string Author { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("homepage")]
    public string? Homepage { get; init; }

    [JsonPropertyName("kafkalensVersion")]
    public string KafkaLensVersion { get; init; } = "";

    [JsonPropertyName("category")]
    public string Category { get; init; } = "";
}
