using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Infrastructure.Identity;

/// <summary>
/// Cuenta de dispositivo del kiosco (Identity §8): no es una persona, no tiene email ni contraseña.
/// El Owner la crea y recibe un código de emparejamiento de un solo uso (vence en 10 minutos); el equipo
/// lo canjea y recibe una sesión con rol <c>kiosk</c>. Solo se guardan los hashes del código y del token
/// de renovación.
/// </summary>
/// <remarks>No implementa <c>ITenantEntity</c>: el canje del código es anónimo (no hay gym en el token
/// todavía), así que el gym se filtra de forma explícita en las operaciones del Owner.</remarks>
public sealed class KioskDevice : BaseEntity
{
    public static readonly TimeSpan PairingCodeLifetime = TimeSpan.FromMinutes(10);

    private KioskDevice()
    {
    }

    public Guid GymId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>El Owner que la creó.</summary>
    public Guid PairedBy { get; private set; }

    public string? PairingCodeHash { get; private set; }

    public DateTimeOffset? PairingCodeExpiresAt { get; private set; }

    public DateTimeOffset? PairedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Hash del token de renovación vigente; <c>null</c> mientras no se empareja y tras revocar.</summary>
    public string? RefreshTokenHash { get; private set; }

    public static KioskDevice Create(Guid gymId, string name, Guid createdBy, string codeHash, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationDomainException("El kiosco necesita un nombre.");
        }

        return new KioskDevice
        {
            GymId = gymId,
            Name = name.Trim(),
            PairedBy = createdBy,
            PairingCodeHash = codeHash,
            PairingCodeExpiresAt = now + PairingCodeLifetime,
        };
    }

    public bool CanPair(DateTimeOffset now)
        => RevokedAt is null && PairedAt is null && PairingCodeHash is not null && PairingCodeExpiresAt > now;

    public void Pair(string refreshTokenHash, DateTimeOffset now)
    {
        PairingCodeHash = null;
        PairingCodeExpiresAt = null;
        PairedAt = now;
        RefreshTokenHash = refreshTokenHash;
    }

    /// <summary>Idempotente. Invalida el código pendiente y la sesión del equipo.</summary>
    public void Revoke(DateTimeOffset now)
    {
        RevokedAt ??= now;
        PairingCodeHash = null;
        PairingCodeExpiresAt = null;
        RefreshTokenHash = null;
    }
}
