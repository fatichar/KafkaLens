using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace KafkaLens.Shared.Services;

/// <summary>
/// Central registry for plugin extension-point implementations.
/// Extensions are registered during plugin loading via <see cref="Register{T}"/>
/// and retrieved with <see cref="GetExtensions{T}"/>.
/// Thread-safe: concurrent reads are permitted; writes acquire an exclusive lock.
/// </summary>
public class ExtensionRegistry : IDisposable
{
    private readonly Dictionary<Type, List<object>> _extensions = new();
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

    /// <summary>Registers an implementation for extension type <typeparamref name="T"/>.</summary>
    public void Register<T>(T impl) where T : notnull
    {
        var key = typeof(T);
        _lock.EnterWriteLock();
        try
        {
            if (!_extensions.TryGetValue(key, out var list))
            {
                list = new List<object>();
                _extensions[key] = list;
            }
            list.Add(impl);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>Returns all registered implementations of <typeparamref name="T"/>.</summary>
    public IReadOnlyList<T> GetExtensions<T>()
    {
        var key = typeof(T);
        _lock.EnterReadLock();
        try
        {
            if (_extensions.TryGetValue(key, out var list))
                return list.OfType<T>().ToList();
            return Array.Empty<T>();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Dispose() => _lock.Dispose();
}
