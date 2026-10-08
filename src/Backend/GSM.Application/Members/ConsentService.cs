using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Members;

namespace GSM.Application.Members;

/// <summary>Lo común a los casos de uso de autorizaciones del staff y del propio miembro.</summary>
public sealed class ConsentService(
    ICurrentUserService currentUser,
    IClock clock,
    IConsentRepository consents,
    IIdentityReader identity,
    MemberChangeRecorder recorder)
{
    public static string FieldFor(ConsentType type) => $"consent_{EnumText.Snake(type)}";

    public static ConsentType ParseType(string? text)
    {
        if (!EnumText.TryParse<ConsentType>(text, out var type) || type is null)
        {
            throw new ValidationDomainException("Tipo de autorización no válido.");
        }

        return type.Value;
    }

    /// <summary>El texto que rige hoy: el último que publicó el gym o, si no hay, el texto base del sistema.</summary>
    public async Task<ConsentDocumentItem> GetCurrentDocumentAsync(ConsentType type, CancellationToken cancellationToken)
    {
        var published = await consents.GetLatestDocumentAsync(type, cancellationToken);
        return published is null
            ? new ConsentDocumentItem(
                EnumText.Snake(type)!, ConsentDocument.BaseVersion, ConsentDefaults.TextFor(type), null, IsDefault: true)
            : new ConsentDocumentItem(
                EnumText.Snake(type)!, published.Version, published.Text, published.EffectiveFrom, IsDefault: false);
    }

    /// <summary>Estado de las dos autorizaciones del miembro frente a la versión vigente.</summary>
    public async Task<IReadOnlyList<MemberConsentStatus>> GetStatusesAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var accepted = await consents.GetForMemberAsync(memberId, cancellationToken);
        var statuses = new List<MemberConsentStatus>();
        foreach (var type in Enum.GetValues<ConsentType>())
        {
            var current = await GetCurrentDocumentAsync(type, cancellationToken);
            var latest = accepted.FirstOrDefault(c => c.Type == type);
            string? byName = null;
            if (latest?.AcceptedByUserId is { } userId
                && await identity.GetUserAsync(userId, cancellationToken) is { } user)
            {
                byName = user.FullName;
            }

            statuses.Add(new MemberConsentStatus(
                current.Type,
                latest is null ? "not_accepted"
                    : latest.IsRevoked ? "revoked"
                    : latest.DocumentVersion == current.Version ? "accepted"
                    : "outdated",
                current.Version,
                current.Text,
                latest?.DocumentVersion,
                latest?.AcceptedAt,
                EnumText.Snake(latest?.Channel),
                latest?.AcceptedByGuardian ?? false,
                latest?.AcceptedByUserId,
                byName,
                latest?.RevokedAt));
        }

        return statuses;
    }

    /// <summary>
    /// Acepta la versión vigente. Aceptar dos veces la misma versión sin revocar es un choque de estado
    /// (409). Deja la fila en el historial; el llamador confirma con su <c>SaveChanges</c>.
    /// </summary>
    public async Task AcceptAsync(
        Member member, ConsentType type, int? requestedVersion, ConsentChannel channel, bool byGuardian,
        CancellationToken cancellationToken)
    {
        var current = await GetCurrentDocumentAsync(type, cancellationToken);
        if (requestedVersion is { } requested && requested != current.Version)
        {
            throw new ConflictException("El texto de la autorización cambió. Léelo de nuevo antes de aceptar.");
        }

        var latest = await consents.GetLatestAsync(member.Id, type, cancellationToken);
        if (latest is { IsRevoked: false } && latest.DocumentVersion == current.Version)
        {
            throw new ConflictException("Esta autorización ya está aceptada en su versión vigente.");
        }

        consents.Add(MemberConsent.Accept(
            member.GymId, member.Id, type, current.Version, clock.UtcNow, channel, currentUser.UserId, byGuardian));

        recorder.Record(member,
        [
            new MemberFieldChange(FieldFor(type), Describe(latest), $"accepted:{current.Version}:{EnumText.Snake(channel)}"),
        ]);
    }

    /// <summary>Revoca la autorización de datos vigente. Sin una aceptada no hay nada que revocar (409).</summary>
    public async Task RevokeAsync(Member member, ConsentType type, CancellationToken cancellationToken)
    {
        if (type != ConsentType.DataProcessing)
        {
            throw new ValidationDomainException("Solo se puede revocar la autorización de tratamiento de datos.");
        }

        var latest = await consents.GetLatestAsync(member.Id, type, cancellationToken);
        if (latest is null or { IsRevoked: true })
        {
            throw new ConflictException("No hay una autorización aceptada para revocar.");
        }

        var before = Describe(latest);
        latest.Revoke(clock.UtcNow);
        recorder.Record(member, [new MemberFieldChange(FieldFor(type), before, $"revoked:{latest.DocumentVersion}")]);
    }

    private static string Describe(MemberConsent? consent) => consent switch
    {
        null => "none",
        { IsRevoked: true } => $"revoked:{consent.DocumentVersion}",
        _ => $"accepted:{consent.DocumentVersion}:{EnumText.Snake(consent.Channel)}",
    };
}
