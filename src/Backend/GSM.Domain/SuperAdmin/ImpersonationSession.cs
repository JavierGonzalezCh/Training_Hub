using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.SuperAdmin;

/// <summary>
/// Auditoría de una sesión de impersonación: qué superadmin miró qué gym, cuándo y por qué.
/// El <see cref="Reason"/> es obligatorio. La identidad real nunca se suplanta (el token mantiene
/// el <c>sub</c> del superadmin); esto solo registra la visita.
/// </summary>
public sealed class ImpersonationSession : BaseEntity
{
    public const int MaxReasonLength = 200;

    private ImpersonationSession()
    {
    }

    public Guid SuperAdminUserId { get; private set; }

    public Guid GymId { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public bool IsActive => EndedAt is null;

    public static ImpersonationSession Open(
        Guid superAdminUserId, Guid gymId, string reason, DateTimeOffset startedAt)
    {
        if (superAdminUserId == Guid.Empty)
        {
            throw new ValidationDomainException("La sesión necesita el superadmin que la abre.");
        }

        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("La sesión necesita el gym visitado.");
        }

        var trimmed = (reason ?? string.Empty).Trim();
        if (trimmed.Length is < 1 or > MaxReasonLength)
        {
            throw new ValidationDomainException(
                $"El motivo de la impersonación es obligatorio y no puede exceder {MaxReasonLength} caracteres.");
        }

        return new ImpersonationSession
        {
            SuperAdminUserId = superAdminUserId,
            GymId = gymId,
            Reason = trimmed,
            StartedAt = startedAt,
        };
    }

    public void End(DateTimeOffset endedAt)
    {
        if (EndedAt is not null)
        {
            throw new ConflictException("La sesión de impersonación ya está cerrada.");
        }

        EndedAt = endedAt;
    }
}
