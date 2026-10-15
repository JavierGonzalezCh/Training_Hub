using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Members;

/// <summary>
/// Registro de quién vio los datos de salud de un miembro (Members v2, §10). Como mucho una fila por
/// usuario, miembro y día (<see cref="AccessedOn"/>). No guarda ningún dato de salud, solo el hecho de
/// la consulta. Solo se añade; el repositorio la escribe con un <c>INSERT … ON CONFLICT DO NOTHING</c>.
/// </summary>
public sealed class MemberHealthAccess : BaseEntity, ITenantEntity
{
    private MemberHealthAccess()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary><c>owner</c> o <c>staff</c> (texto simple: la tabla no depende de enums de Postgres).</summary>
    public string Role { get; private set; } = string.Empty;

    public DateTimeOffset AccessedAt { get; private set; }

    /// <summary>Día de negocio (zona del gym) de la consulta; con usuario y miembro forma la clave única.</summary>
    public DateOnly AccessedOn { get; private set; }

    public static MemberHealthAccess Record(
        Guid gymId, Guid memberId, Guid userId, string role, DateTimeOffset accessedAt, DateOnly accessedOn)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty || userId == Guid.Empty || string.IsNullOrWhiteSpace(role))
        {
            throw new ValidationDomainException("Registro de consulta de salud inválido.");
        }

        return new MemberHealthAccess
        {
            GymId = gymId,
            MemberId = memberId,
            UserId = userId,
            Role = role,
            AccessedAt = accessedAt,
            AccessedOn = accessedOn,
        };
    }
}
