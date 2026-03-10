using System;

namespace KafkaLens.Shared.Plugins;

/// <summary>
/// Apply to a class to declare it as an extension point implementation.
/// The <see cref="ExtensionType"/> is the interface this class implements
/// (e.g. <c>typeof(IMessageFormatter)</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class KafkaLensExtensionAttribute : Attribute
{
    public Type ExtensionType { get; }

    public KafkaLensExtensionAttribute(Type extensionType)
    {
        ExtensionType = extensionType;
    }
}
