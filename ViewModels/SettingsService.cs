using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using KafkaLens.Shared.Models;

namespace KafkaLens.ViewModels;

public class SettingsService : ISettingsService
{
    private readonly string filePath;
    private JObject settings = new();
    private bool defaultsAddedDuringLoad;

    public SettingsService(string filePath)
    {
        this.filePath = filePath;
        Load();
    }

    private void Load()
    {
        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                settings = JsonConvert.DeserializeObject<JObject>(json) ?? new JObject();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to load settings from {FilePath}", filePath);
                settings = new JObject();
            }
        }

        EnsureDefaults();

        if (defaultsAddedDuringLoad)
        {
            Save();
        }
    }

    private void Save()
    {
        try
        {
            var json = settings.ToString(Newtonsoft.Json.Formatting.Indented);
            var directory = Path.GetDirectoryName(filePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save settings to {FilePath}", filePath);
        }
    }

    private void EnsureDefaults()
    {
        defaultsAddedDuringLoad = false;
        MergeDefaults("Theme", JValue.CreateString("System"));
        MergeDefaults("AutoCheckForUpdates", JValue.CreateString("true"));
        MergeDefaults("HiddenKeyFormatters", JValue.CreateString("[]"));
        MergeDefaults("HiddenValueFormatters", JValue.CreateString("[]"));
        MergeDefaults(nameof(KafkaConfig), JObject.FromObject(new KafkaConfig()));
        MergeDefaults(nameof(BrowserConfig), JObject.FromObject(new BrowserConfig()));
        MergeDefaults(nameof(PluginSettings), JObject.FromObject(new PluginSettings()));
        EnsurePluginSettingsDefaults();
    }

    private void EnsurePluginSettingsDefaults()
    {
        if (!settings.TryGetValue(nameof(PluginSettings), out var token) || token is not JObject pluginSettings)
        {
            return;
        }

        var defaultRepositories = new PluginSettings().Repositories;
        if (defaultRepositories.Count == 0)
        {
            return;
        }

        if (!pluginSettings.TryGetValue(nameof(PluginSettings.Repositories), out var repositoriesToken) ||
            repositoriesToken.Type == JTokenType.Null)
        {
            pluginSettings[nameof(PluginSettings.Repositories)] = JArray.FromObject(defaultRepositories);
            defaultsAddedDuringLoad = true;
            return;
        }

        if (repositoriesToken is JArray repositoriesArray && repositoriesArray.Count == 0)
        {
            pluginSettings[nameof(PluginSettings.Repositories)] = JArray.FromObject(defaultRepositories);
            defaultsAddedDuringLoad = true;
        }
    }

    private void MergeDefaults(string key, JToken defaultValue)
    {
        if (!settings.TryGetValue(key, out var existingValue) || existingValue.Type == JTokenType.Null)
        {
            settings[key] = defaultValue.DeepClone();
            defaultsAddedDuringLoad = true;
            return;
        }

        if (existingValue.Type == JTokenType.Object && defaultValue.Type == JTokenType.Object)
        {
            if (MergeObjectDefaults((JObject)existingValue, (JObject)defaultValue))
            {
                defaultsAddedDuringLoad = true;
            }
        }
    }

    private static bool MergeObjectDefaults(JObject target, JObject defaults)
    {
        var changed = false;

        foreach (var property in defaults.Properties())
        {
            if (!target.TryGetValue(property.Name, out var existingValue) || existingValue.Type == JTokenType.Null)
            {
                target[property.Name] = property.Value.DeepClone();
                changed = true;
                continue;
            }

            if (existingValue.Type == JTokenType.Object && property.Value.Type == JTokenType.Object)
            {
                changed |= MergeObjectDefaults((JObject)existingValue, (JObject)property.Value);
            }
        }

        return changed;
    }

    public string? GetValue(string key)
    {
        if (!settings.TryGetValue(key, out var value))
        {
            return null;
        }

        return value.Type switch
        {
            JTokenType.String => value.Value<string>(),
            JTokenType.Array => string.Join(",", value.Values<string>().Where(v => !string.IsNullOrWhiteSpace(v))),
            JTokenType.Null => null,
            _ => value.ToString()
        };
    }

    public void SetValue(string key, string value)
    {
        settings[key] = JValue.CreateString(value);
        Save();
    }

    public KafkaConfig GetKafkaConfig()
    {
        if (settings.TryGetValue(nameof(KafkaConfig), out var token) && token is JObject obj)
        {
            return obj.ToObject<KafkaConfig>() ?? new KafkaConfig();
        }
        return new KafkaConfig();
    }

    public void SaveKafkaConfig(KafkaConfig config)
    {
        settings[nameof(KafkaConfig)] = JObject.FromObject(config);
        Save();
    }

    public BrowserConfig GetBrowserConfig()
    {
        if (settings.TryGetValue(nameof(BrowserConfig), out var token) && token is JObject obj)
        {
            return obj.ToObject<BrowserConfig>() ?? new BrowserConfig();
        }
        return new BrowserConfig();
    }

    public void SaveBrowserConfig(BrowserConfig config)
    {
        settings[nameof(BrowserConfig)] = JObject.FromObject(config);
        Save();
    }

    private static readonly JsonSerializer ReplaceSerializer = new()
    {
        ObjectCreationHandling = ObjectCreationHandling.Replace
    };

    public PluginSettings GetPluginSettings()
    {
        if (settings.TryGetValue(nameof(PluginSettings), out var token) && token is JObject obj)
            return obj.ToObject<PluginSettings>(ReplaceSerializer) ?? new PluginSettings();
        return new PluginSettings();
    }

    public void SavePluginSettings(PluginSettings ps)
    {
        settings[nameof(PluginSettings)] = JObject.FromObject(ps);
        Save();
    }
}
