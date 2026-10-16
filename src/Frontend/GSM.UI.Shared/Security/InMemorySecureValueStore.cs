using System.Collections.Concurrent;

namespace GSM.UI.Shared.Security;

/// <summary>Almacén en memoria: valor por defecto (web y pruebas). El host nativo registra el suyo sobre SecureStorage.</summary>
public sealed class InMemorySecureValueStore : ISecureValueStore
{
    private readonly ConcurrentDictionary<string, string> _values = new();

    public Task<string?> GetAsync(string key) => Task.FromResult(_values.TryGetValue(key, out var v) ? v : null);

    public Task SetAsync(string key, string value)
    {
        _values[key] = value;
        return Task.CompletedTask;
    }

    public void Remove(string key) => _values.TryRemove(key, out _);
}
