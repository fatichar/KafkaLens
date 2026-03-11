using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KafkaLens.Shared.Plugins;
using Serilog;

namespace KafkaLens.ViewModels.Services;

public class PluginRepositoryClient
{
    // Shared across all instances to avoid socket exhaustion.
    private static readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _appVersion;

    static PluginRepositoryClient()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("KafkaLens", "1.0"));
    }

    public PluginRepositoryClient()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        _appVersion = version != null
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "0.0.0";
    }

    /// <summary>
    /// Fetches and parses a plugin repository index, filtering to only compatible plugins.
    /// Returns <c>null</c> on network or parse error.
    /// </summary>
    public async Task<RepositoryIndex?> FetchAsync(string url, CancellationToken ct = default)
    {
        try
        {
            var json  = await _http.GetStringAsync(url, ct);
            var index = JsonSerializer.Deserialize<RepositoryIndex>(json, JsonOptions);
            if (index == null)
            {
                Log.Warning("Repository at {Url} returned empty or unparseable JSON", url);
                return null;
            }

            var compatible = index.Plugins
                .Where(p => VersionCompatibility.IsCompatible(p.KafkaLensVersion, _appVersion))
                .ToList();

            return new RepositoryIndex
            {
                Name    = index.Name,
                Plugins = compatible
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to fetch plugin repository from {Url}", url);
            return null;
        }
    }
}
