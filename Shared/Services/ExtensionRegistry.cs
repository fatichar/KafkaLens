using System;
using System.Collections.Generic;
using System.Linq;

namespace KafkaLens.Shared.Services;

/// <summary>
/// Central registry for plugin extension-point implementations.
/// Extensions are registered during plugin loading via
/// <see cref="Register{T}"/> and retrieved with <see cref="GetExtensions{T}"/>.
/// </summary>
public class ExtensionRegistry
{
    private readonly Dictionary<Type, List<object>> _extensions = new();

    /// <summary>Registers an implementation for extension type <typeparamref name="T"/>.</summary>
    public void Register<T>(T impl) where T : notnull
    {
        var key = typeof(T);
        if (!_extensions.TryGetValue(key, out var list))
        {
            list = new List<object>();
            _extensions[key] = list;
        }
        list.Add(impl);
    }

    /// <summary>Returns all registered implementations of <typeparamref name="T"/>.</summary>
    public IReadOnlyList<T> GetExtensions<T>()
    {
        var key = typeof(T);
        if (_extensions.TryGetValue(key, out var list))
            return list.OfType<T>().ToList();
        return Array.Empty<T>();
    }
}
