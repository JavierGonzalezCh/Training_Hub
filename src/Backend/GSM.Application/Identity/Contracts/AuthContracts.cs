namespace GSM.Application.Identity.Contracts;

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

/// <summary>Par de tokens que devuelven login y refresh.</summary>
public sealed record AuthTokensResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    string TokenType = "Bearer");

/// <summary>Perfil del usuario autenticado. Los feature flags se agregan en la etapa 2 (módulo Gyms).</summary>
public sealed record MeResponse(
    Guid UserId,
    string FullName,
    string Email,
    string Role,
    Guid? GymId);
