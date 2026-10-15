using GSM.Domain.Common;

namespace GSM.Infrastructure.Identity;

/// <summary>
/// Código de 6 dígitos para entrar sin contraseña (Identity §6). Solo se guarda el hash (HMAC con la clave
/// de firma y el id de la persona): el código en claro viaja una vez, por correo. Vence a los 10 minutos,
/// sirve una sola vez (<see cref="ConsumedAt"/>) y admite <see cref="MaxAttempts"/> intentos.
/// </summary>
public sealed class LoginCode : BaseEntity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public const int MaxAttempts = 5;

    /// <summary>Mínimo entre dos códigos pedidos por la misma persona.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(1);

    /// <summary>Tope de códigos pedidos por persona en la última hora.</summary>
    public const int MaxPerHour = 5;

    private LoginCode()
    {
    }

    public Guid UserId { get; private set; }

    public string CodeHash { get; private set; } = string.Empty;

    /// <summary>Instante en que se emitió (reloj del sistema, <c>IClock</c>); sirve para los límites de petición.</summary>
    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public int Attempts { get; private set; }

    /// <summary>Cuándo se usó, se agotó o lo reemplazó un código nuevo. Nulo mientras sigue vigente.</summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    public static LoginCode Issue(Guid userId, string codeHash, DateTimeOffset now) => new()
    {
        UserId = userId,
        CodeHash = codeHash,
        IssuedAt = now,
        ExpiresAt = now + Lifetime,
    };
}
