namespace GSM.Application.Identity.Abstractions;

/// <summary>
/// Cuentas de dispositivo del kiosco (Identity §8). Implementado en Infrastructure. Las operaciones del
/// Owner reciben el gym explícito; el canje del código y la renovación son anónimos y resuelven el gym
/// desde el propio dispositivo.
/// </summary>
public interface IKioskDeviceService
{
    Task<IReadOnlyList<KioskDeviceRecord>> ListAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>Crea el kiosco con un código de emparejamiento de 10 minutos; devuelve el código en claro (solo esta vez).</summary>
    Task<(KioskDeviceRecord Device, string PairingCode)> CreateAsync(
        Guid gymId, string name, Guid createdBy, CancellationToken cancellationToken = default);

    /// <summary>Revoca el kiosco (cierra su sesión y anula el código pendiente). Idempotente. 404 si no es de ese gym.</summary>
    Task<KioskDeviceRecord> RevokeAsync(Guid gymId, Guid deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Canjea el código: lo consume y devuelve el dispositivo con el token de renovación en claro.
    /// <c>null</c> si el código no existe, venció o ya se usó.
    /// </summary>
    Task<KioskPairing?> PairAsync(string pairingCode, CancellationToken cancellationToken = default);

    /// <summary>El dispositivo dueño del token de renovación, si no está revocado; si no, <c>null</c>.</summary>
    Task<KioskDeviceRecord?> ResolveActiveAsync(string refreshToken, CancellationToken cancellationToken = default);
}

/// <param name="Status"><c>pending</c> (código vigente), <c>expired</c> (código vencido sin canjear), <c>paired</c> o <c>revoked</c>.</param>
public sealed record KioskDeviceRecord(
    Guid Id,
    Guid GymId,
    string Name,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PairingCodeExpiresAt,
    DateTimeOffset? PairedAt,
    DateTimeOffset? RevokedAt);

public sealed record KioskPairing(KioskDeviceRecord Device, string RefreshToken);
