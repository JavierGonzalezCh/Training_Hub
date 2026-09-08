using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Members;

/// <summary>
/// Código de un solo uso para que un miembro registrado en recepción cree su cuenta en la app
/// y quede vinculado (<see cref="Member.LinkUser"/>). Expira; al canjearlo queda marcado.
/// </summary>
/// <remarks>
/// A diferencia de los refresh tokens, el <see cref="Code"/> se guarda en claro. Asimetría
/// deliberada: vive 7 días, es de un solo uso y el canje exige además el documento como
/// segundo factor. Si el modelo de amenaza cambia (p. ej. envío por canal inseguro), hashearlo.
/// </remarks>
public sealed class MemberInvitation : BaseEntity, ITenantEntity
{
    private MemberInvitation()
    {
    }

    public Guid MemberId { get; private set; }

    public Guid GymId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RedeemedAt { get; private set; }

    public Guid? RedeemedByUserId { get; private set; }

    public static MemberInvitation Issue(Guid memberId, Guid gymId, string code, DateTimeOffset expiresAt)
    {
        if (memberId == Guid.Empty || gymId == Guid.Empty)
        {
            throw new ValidationDomainException("Invitación inválida.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ValidationDomainException("La invitación necesita un código.");
        }

        return new MemberInvitation
        {
            MemberId = memberId,
            GymId = gymId,
            Code = code,
            ExpiresAt = expiresAt,
        };
    }

    public bool IsPendingAt(DateTimeOffset now) => RedeemedAt is null && ExpiresAt > now;

    public void Redeem(Guid userId, DateTimeOffset now)
    {
        if (!IsPendingAt(now))
        {
            throw new ConflictException("La invitación ya se usó o expiró.");
        }

        RedeemedAt = now;
        RedeemedByUserId = userId;
    }
}
