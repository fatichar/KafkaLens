using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KafkaLens.Shared.Plugins;

public sealed class RepositoryIndex
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("plugins")]
    public List<RepositoryPlugin> Plugins { get; init; } = [];
}

public sealed class RepositoryPlugin
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("author")]
    public string Author { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("kafkalensVersion")]
    public string KafkaLensVersion { get; init; } = "";

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; init; } = "";

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; init; }
}
