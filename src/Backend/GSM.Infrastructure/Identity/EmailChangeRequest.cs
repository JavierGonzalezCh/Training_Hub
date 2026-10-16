using GSM.Domain.Common;

namespace GSM.Infrastructure.Identity;

/// <summary>
/// Pedido de cambio de email (Identity §3.4): un código de 6 dígitos viaja al correo NUEVO y otro al ACTUAL (I6-1:
/// una sesión robada no basta para quedarse con la cuenta); de los dos solo se guarda el hash (HMAC con la clave de
/// firma, el id de la persona y el email nuevo). Vence a los 10 minutos, sirve
/// una sola vez y admite <see cref="MaxAttempts"/> intentos. Un pedido nuevo anula el anterior.
/// </summary>
public sealed class EmailChangeRequest : BaseEntity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public const int MaxAttempts = 5;

    /// <summary>Mínimo entre dos pedidos de la misma persona.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(1);

    /// <summary>Tope de pedidos por persona en la última hora.</summary>
    public const int MaxPerHour = 5;

    private EmailChangeRequest()
    {
    }

    public Guid UserId { get; private set; }

    /// <summary>El email al que se quiere cambiar, sin espacios.</summary>
    public string NewEmail { get; private set; } = string.Empty;

    public string CodeHash { get; private set; } = string.Empty;

    /// <summary>Hash del código que se mandó al correo actual de la cuenta.</summary>
    public string CurrentCodeHash { get; private set; } = string.Empty;

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public int Attempts { get; private set; }

    /// <summary>Cuándo se usó, se agotó o lo reemplazó un pedido nuevo. Nulo mientras sigue vigente.</summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    public static EmailChangeRequest Issue(
        Guid userId, string newEmail, string codeHash, string currentCodeHash, DateTimeOffset now) => new()
    {
        UserId = userId,
        NewEmail = newEmail,
        CodeHash = codeHash,
        CurrentCodeHash = currentCodeHash,
        IssuedAt = now,
        ExpiresAt = now + Lifetime,
    };
}
