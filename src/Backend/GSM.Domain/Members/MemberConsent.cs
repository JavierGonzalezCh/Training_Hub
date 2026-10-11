using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Members;

/// <summary>
/// Aceptación de una autorización por un miembro (Members v2, §3.4). Una fila por aceptación: quien
/// acepta una versión nueva, o vuelve a aceptar tras revocar, agrega otra fila; la vigente es la más
/// reciente. Revocar solo marca <see cref="RevokedAt"/>: la fecha, el tipo y la versión quedan como
/// prueba de que la autorización existió.
/// </summary>
public sealed class MemberConsent : BaseEntity, ITenantEntity
{
    private MemberConsent()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    public ConsentType Type { get; private set; }

    /// <summary>Versión del texto que aceptó (<see cref="ConsentDocument.BaseVersion"/> = texto base).</summary>
    public int DocumentVersion { get; private set; }

    public DateTimeOffset AcceptedAt { get; private set; }

    public ConsentChannel Channel { get; private set; }

    /// <summary>Quien la registró: el propio miembro en la app, o el staff en recepción.</summary>
    public Guid? AcceptedByUserId { get; private set; }

    /// <summary>La aceptó el acudiente de un menor de edad.</summary>
    public bool AcceptedByGuardian { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public static MemberConsent Accept(
        Guid gymId, Guid memberId, ConsentType type, int documentVersion, DateTimeOffset acceptedAt,
        ConsentChannel channel, Guid? acceptedByUserId, bool acceptedByGuardian)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty || documentVersion < ConsentDocument.BaseVersion)
        {
            throw new ValidationDomainException("Autorización inválida.");
        }

        return new MemberConsent
        {
            GymId = gymId,
            MemberId = memberId,
            Type = type,
            DocumentVersion = documentVersion,
            AcceptedAt = acceptedAt,
            Channel = channel,
            AcceptedByUserId = acceptedByUserId,
            AcceptedByGuardian = acceptedByGuardian,
        };
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        if (Type != ConsentType.DataProcessing)
        {
            throw new ValidationDomainException("Solo se puede revocar la autorización de tratamiento de datos.");
        }

        if (IsRevoked)
        {
            throw new ConflictException("La autorización ya estaba revocada.");
        }

        RevokedAt = revokedAt;
    }
}
