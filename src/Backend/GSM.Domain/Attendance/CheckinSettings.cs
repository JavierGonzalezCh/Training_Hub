using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Attendance;

/// <summary>
/// Ajustes de check-in del gym. Uno por gym; si no hay fila, valen los de <see cref="Default"/>.
/// Los días de gracia ya no viven aquí: pasaron a <c>memberships.membership_settings</c> (MS1).
/// </summary>
public sealed class CheckinSettings : BaseEntity, ITenantEntity
{
    private CheckinSettings()
    {
    }

    public Guid GymId { get; private set; }

    public CheckinMode Mode { get; private set; }

    /// <summary>Versión del código fijo. Regenerar la incrementa y el cartel anterior deja de servir.</summary>
    public int StaticCodeVersion { get; private set; }

    /// <summary>Entrada con documento en el kiosco (A4). Apagada por defecto.</summary>
    public bool ByDocument { get; private set; }

    /// <summary>
    /// Cuánto se asume que una persona se queda en el gym tras entrar (A5). Con eso se estima la ocupación; la
    /// salida no se registra. 120 por defecto: más de lo normal, para tener margen.
    /// </summary>
    public int AssumedStayMinutes { get; private set; } = DefaultAssumedStayMinutes;

    public const int DefaultAssumedStayMinutes = 120;

    public const int MinAssumedStayMinutes = 15;

    public const int MaxAssumedStayMinutes = 480;

    public static CheckinSettings Default(Guid gymId)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("Gym inválido.");
        }

        return new CheckinSettings { GymId = gymId, Mode = CheckinMode.Rotating, StaticCodeVersion = 1, AssumedStayMinutes = DefaultAssumedStayMinutes };
    }

    public void Update(CheckinMode mode, bool? byDocument = null, int? assumedStayMinutes = null)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ValidationDomainException("Modo de check-in inválido.");
        }

        if (assumedStayMinutes is < MinAssumedStayMinutes or > MaxAssumedStayMinutes)
        {
            throw new ValidationDomainException(
                $"La permanencia asumida debe estar entre {MinAssumedStayMinutes} y {MaxAssumedStayMinutes} minutos.");
        }

        Mode = mode;
        if (byDocument is { } enabled)
        {
            ByDocument = enabled;
        }

        if (assumedStayMinutes is { } stay)
        {
            AssumedStayMinutes = stay;
        }
    }

    public void RegenerateStaticCode() => StaticCodeVersion++;
}
