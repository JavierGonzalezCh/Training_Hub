using GSM.Application.Common;

namespace GSM.Application.Identity.Contracts;

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

/// <summary>Par de tokens que devuelven login y refresh.</summary>
public sealed record AuthTokensResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    string TokenType = "Bearer");

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
    bool CommunityComments);
