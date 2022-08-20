﻿using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace KafkaLens.App
{
    public class SettingsService : ISettingsService
    {
        private readonly Dictionary<string, string> settings = new();
        
        public string? GetValue(string key)
        {
            settings.TryGetValue(key, out string? val);
            return val;
        }
        
        public void SetValue(string key, string value)
        {
            settings[key] = value;
        }
    }
}