using Newtonsoft.Json;
using System.IO;

namespace KafkaLens.ViewModels;

public class TopicSettingsService : ITopicSettingsService
{
    private readonly string filePath;
    // Map: ClusterId -> TopicName -> Settings
    private Dictionary<string, Dictionary<string, TopicSettings>> clusterSettings = new();
    // Map: TopicName -> Settings
    private Dictionary<string, TopicSettings> globalSettings = new();

    public TopicSettingsService(string filePath)
    {
        this.filePath = filePath;
        Load();
    }

    private class PersistenceModel
    {
        public Dictionary<string, Dictionary<string, TopicSettings>> ClusterSettings { get; set; } = new();
        public Dictionary<string, TopicSettings> GlobalSettings { get; set; } = new();
    }

    private void Load()
    {
        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var model = JsonConvert.DeserializeObject<PersistenceModel>(json);
                if (model != null)
                {
                    clusterSettings = model.ClusterSettings ?? new();
                    globalSettings = model.GlobalSettings ?? new();
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to load topic settings from {FilePath}", filePath);
            }
        }
    }

    private void Save()
    {
        try
        {
            var model = new PersistenceModel
            {
                ClusterSettings = clusterSettings,
                GlobalSettings = globalSettings
            };
            var json = JsonConvert.SerializeObject(model, Newtonsoft.Json.Formatting.Indented);
            var directory = Path.GetDirectoryName(filePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save topic settings to {FilePath}", filePath);
        }
    }

    public TopicSettings GetSettings(string clusterId, string topicName)
    {
        if (clusterSettings.TryGetValue(clusterId, out var topicMap) && topicMap.TryGetValue(topicName, out var settings))
        {
            return new TopicSettings
            {
                KeyFormatter = settings.KeyFormatter,
                ValueFormatter = settings.ValueFormatter
            };
        }

        if (globalSettings.TryGetValue(topicName, out var globalSetting))
        {
            return new TopicSettings
            {
                KeyFormatter = globalSetting.KeyFormatter,
                ValueFormatter = globalSetting.ValueFormatter
            };
        }

        return new TopicSettings();
    }

    public void SetSettings(string clusterId, string topicName, TopicSettings settings, bool applyToAllClusters = false)
    {
        if (!clusterSettings.TryGetValue(clusterId, out var topicMap))
        {
            topicMap = new Dictionary<string, TopicSettings>();
            clusterSettings[clusterId] = topicMap;
        }

        topicMap[topicName] = new TopicSettings
        {
            KeyFormatter = settings.KeyFormatter,
            ValueFormatter = settings.ValueFormatter
        };

        if (applyToAllClusters)
        {
            globalSettings[topicName] = new TopicSettings
            {
                KeyFormatter = settings.KeyFormatter,
                ValueFormatter = settings.ValueFormatter
            };
        }

        Save();
    }
}
