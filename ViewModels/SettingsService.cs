using Newtonsoft.Json;
using System.IO;

namespace KafkaLens.ViewModels;

public class SettingsService : ISettingsService
{
    private readonly string filePath;
    private Dictionary<string, string> settings = new();

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
                settings = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to load settings from {FilePath}", filePath);
                settings = new();
            }
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonConvert.SerializeObject(settings, Newtonsoft.Json.Formatting.Indented);
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

    public string? GetValue(string key)
    {
        settings.TryGetValue(key, out var val);
        return val;
    }

    public void SetValue(string key, string value)
    {
        settings[key] = value;
        Save();
    }
}
