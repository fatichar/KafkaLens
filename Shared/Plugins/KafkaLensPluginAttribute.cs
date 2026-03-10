using System;

namespace KafkaLens.Shared.Plugins;

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class KafkaLensPluginAttribute : Attribute
{
    public string Id          { get; set; } = "";
    public string Name        { get; set; } = "";
    public string Version     { get; set; } = "";
    public string Author      { get; set; } = "";
    public string Description { get; set; } = "";
}
