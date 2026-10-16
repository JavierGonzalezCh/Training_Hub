using GSM.Application.Common;

namespace GSM.Application.Identity.Contracts;

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

/// <summary>
/// Activar una cuenta invitada con el enlace <c>/activar?email=…&amp;code=…</c>: <c>Code</c> es el del enlace.
/// </summary>
public sealed record ActivateAccountRequest(string Email, string Code, string NewPassword);

/// <summary>
/// Cambiar la contraseña con la sesión iniciada. <c>RefreshToken</c> es el de la sesión actual: es la única
/// que sobrevive; las demás sesiones de la persona (en todos sus gyms) se cierran. Sin él se cierran todas.
/// </summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword, string? RefreshToken = null);

/// <summary>Un enlace listo para copiar. <c>RequiresLogin</c>: la persona ya tiene contraseña y el enlace la lleva a entrar.</summary>
public sealed record AccountLinkResponse(string Url, bool RequiresLogin, DateTimeOffset? ExpiresAt);

/// <summary>Owner de un gym, para la ficha del SuperAdmin. <c>IsActivated</c>: ya definió su contraseña.</summary>
public sealed record GymOwnerResponse(Guid UserId, string Email, string FullName, bool IsActive, bool IsActivated);

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

/// <summary>
/// Pide un código de 6 dígitos por correo para entrar sin contraseña. <c>GymSlug</c> (opcional) pone el nombre
/// del gym en el correo. La respuesta es siempre la misma exista o no la cuenta.
/// </summary>
public sealed record RequestLoginCodeRequest(string Email, string? GymSlug = null);

/// <summary>Entra con el código recibido por correo. La respuesta es la del login (<see cref="LoginResponse"/>).</summary>
public sealed record VerifyLoginCodeRequest(string Email, string Code);

/// <summary>
/// Pide cambiar el email de la cuenta (Identity §3.4): manda un código de 6 dígitos al correo NUEVO. El email
/// no cambia hasta confirmarlo con <see cref="ConfirmEmailChangeRequest"/>.
/// </summary>
public sealed record RequestEmailChangeRequest(string NewEmail);

/// <summary>Confirma el cambio de email con el código recibido en el correo nuevo.</summary>
public sealed record ConfirmEmailChangeRequest(string Code);

/// <summary>El email que quedó en la cuenta tras confirmar el cambio.</summary>
public sealed record EmailChangedResponse(string Email);

/// <summary>Marca pública del gym para la pantalla de entrada del miembro (sin sesión).</summary>
public sealed record PublicGymBrandingResponse(string Name, string? LogoUrl, string PrimaryColor, string SecondaryColor);

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
    bool MembersCanCreateRoutines,
    bool MembersCanReportEquipment,
    bool ShowRanking);
