using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace KafkaLens.App
{
    public class SettingsService : ISettingsService
    {
        private readonly Dictionary<string, object> settings = new();
        
        public T GetValue<T>([NotNull]string key)
        {
            object val = settings.GetValueOrDefault(key, default(T));
            return (T)val;
        }
        
        public void SetValue<T>(string key, T value)
        {
            throw new System.NotImplementedException();
        }
    }
}