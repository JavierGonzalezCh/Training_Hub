namespace GSM.Client.Http;

/// <summary>
/// Par de tokens que devuelven <c>/api/auth/login</c> y <c>/api/auth/refresh</c>.
/// El refresh token rota: cada refresh revoca el anterior.
/// </summary>
public sealed record AuthTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Almacén del par de tokens de la sesión. Su implementación depende del host:
/// <c>localStorage</c> en WebAssembly, <c>SecureStorage</c> en MAUI.
/// </summary>
public interface ITokenStore
{
    ValueTask<AuthTokens?> GetAsync(CancellationToken cancellationToken = default);

    ValueTask SetAsync(AuthTokens tokens, CancellationToken cancellationToken = default);

    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>Almacén en memoria; sirve para tests y como valor por defecto.</summary>
public sealed class InMemoryTokenStore : ITokenStore
{
    private AuthTokens? _tokens;

    public ValueTask<AuthTokens?> GetAsync(CancellationToken cancellationToken = default) => new(_tokens);

    public ValueTask SetAsync(AuthTokens tokens, CancellationToken cancellationToken = default)
    {
        _tokens = tokens;
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        _tokens = null;
        return ValueTask.CompletedTask;
    }
}
