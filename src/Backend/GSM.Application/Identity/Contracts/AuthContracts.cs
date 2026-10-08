using GSM.Application.Common;

namespace GSM.Application.Identity.Contracts;

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

/// <summary>Par de tokens que devuelven login, refresh, selección de gym y cambio de gym.</summary>
public sealed record AuthTokensResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    string TokenType = "Bearer");

/// <summary>
/// Resultado del login. Con un solo acceso (o el SuperAdmin) trae <see cref="Tokens"/> y la persona entra
/// directo. Con varios, <see cref="RequiresGymSelection"/> es <c>true</c> y trae el boleto y la lista de
/// accesos (el último usado primero): la persona elige y llama a <c>/api/auth/select-gym</c>.
/// </summary>
public sealed record LoginResponse(
    bool RequiresGymSelection,
    AuthTokensResponse? Tokens = null,
    string? SelectionToken = null,
    IReadOnlyList<GymAccessOption>? Accesses = null);

/// <summary>Un acceso de la persona a un gym. <c>Role</c> es <c>owner</c>, <c>staff</c> o <c>member</c>.</summary>
public sealed record GymAccessOption(
    Guid AccessId,
    Guid GymId,
    string GymName,
    string? LogoUrl,
    string Role,
    DateTimeOffset? LastUsedAt,
    bool IsCurrent = false);

public sealed record SelectGymRequest(string SelectionToken, Guid AccessId);

/// <summary>Cambiar de gym (o de modo en el mismo gym) sin pedir contraseña. <c>RefreshToken</c> es el de la sesión actual, que se revoca.</summary>
public sealed record SwitchGymRequest(Guid AccessId, string? RefreshToken = null);

/// <summary>Perfil del usuario autenticado. <c>Gym</c> y <c>FeatureFlags</c> vienen solo si el usuario tiene gym.</summary>
public sealed record MeResponse(
    Guid UserId,
    string FullName,
    string Email,
    string Role,
    Guid? GymId,
    MeGym? Gym = null,
    MeFeatureFlags? FeatureFlags = null,
    SubscriptionInfo? Subscription = null);

public sealed record MeGym(string Name, string? LogoUrl, string PrimaryColor, string SecondaryColor);

public sealed record MeFeatureFlags(
    bool ShowRoutines,
    bool ShowClasses,
    bool ShowProgress,
    bool ShowGamification,
    bool ShowCommunity,
    bool CheckinQr,
    bool CommunityComments,
    bool MembersCanCreateRoutines);
