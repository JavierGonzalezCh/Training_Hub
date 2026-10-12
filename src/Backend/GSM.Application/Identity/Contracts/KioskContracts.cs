namespace GSM.Application.Identity.Contracts;

public sealed record CreateKioskRequest(string Name);

/// <summary>
/// <c>Status</c>: <c>pending</c> (código vigente), <c>expired</c> (código vencido sin canjear),
/// <c>paired</c> o <c>revoked</c>.
/// </summary>
public sealed record KioskDeviceResponse(
    Guid Id,
    string Name,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PairingCodeExpiresAt,
    DateTimeOffset? PairedAt,
    DateTimeOffset? RevokedAt);

/// <summary>Kiosco recién creado con su código de emparejamiento de 8 caracteres (solo se muestra esta vez).</summary>
public sealed record KioskCreatedResponse(KioskDeviceResponse Device, string PairingCode, DateTimeOffset ExpiresAt);

/// <summary>El equipo del kiosco canjea el código que le dio el Owner.</summary>
public sealed record PairKioskRequest(string PairingCode);

/// <summary>El equipo renueva su sesión con el token de renovación que recibió al emparejarse.</summary>
public sealed record KioskRefreshRequest(string RefreshToken);
