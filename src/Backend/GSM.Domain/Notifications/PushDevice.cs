using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Notifications;

/// <summary>
/// Dónde está instalado el dispositivo que recibe push. Label en Postgres: <c>notifications.push_platform</c>.
/// <c>ios</c> llegará con la app de iOS.
/// </summary>
public enum PushPlatform
{
    Android,
    WebMember,
    WebPanel,
}

/// <summary>
/// Un dispositivo que puede recibir push (spec Notifications §6). <b>Es de la persona, no del gym</b>: no
/// lleva <c>GymId</c> ni Global Query Filter, para que quien es miembro en un gym y entrenador en otro
/// reciba los avisos de ambos. El token es único: registrarlo otra vez no duplica, lo reutiliza.
/// </summary>
public sealed class PushDevice : BaseEntity
{
    public const int TokenMaxLength = 4096;

    private PushDevice()
    {
    }

    public Guid UserId { get; private set; }

    public string Token { get; private set; } = string.Empty;

    public PushPlatform Platform { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>Se llena al darlo de baja o cuando FCM dice que el token ya no sirve.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    public static PushDevice Register(Guid userId, string token, PushPlatform platform, DateTimeOffset now)
    {
        var device = new PushDevice();
        device.Apply(userId, token, platform, now);
        return device;
    }

    /// <summary>Un token ya conocido se reasigna a quien lo registra (el mismo teléfono con otra cuenta) y se reactiva.</summary>
    public void Reregister(Guid userId, string token, PushPlatform platform, DateTimeOffset now)
        => Apply(userId, token, platform, now);

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    private void Apply(Guid userId, string token, PushPlatform platform, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new ValidationDomainException("El dispositivo necesita una persona.");
        }

        var normalized = (token ?? string.Empty).Trim();
        if (normalized.Length is 0 or > TokenMaxLength)
        {
            throw new ValidationDomainException("El token del dispositivo no es válido.");
        }

        UserId = userId;
        Token = normalized;
        Platform = platform;
        LastSeenAt = now;
        RevokedAt = null;
    }
}
