using System.Collections.Generic;

namespace KafkaLens.App
{
    public class SettingsService : ISettingsService
    {
        private readonly Dictionary<string, string> settings = new();
        
        public string? GetValue(string key)
        {
            settings.TryGetValue(key, out var val);
            return val;
        }
        
        public void SetValue(string key, string value)
        {
            settings[key] = value;
        }
    }
}