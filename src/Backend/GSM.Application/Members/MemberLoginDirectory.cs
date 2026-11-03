using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Events;
using GSM.Domain.Members;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Members;

/// <summary>
/// FL1: Members responde al login por celular y al primer ingreso con el documento
/// (<see cref="IMemberLoginDirectory"/>, puerto de Identity). El alta reusa el camino del canje de la invitación:
/// <see cref="IUserProvisioning"/> crea la cuenta y la ficha se vincula en la misma transacción.
/// </summary>
public sealed class MemberLoginDirectory(
    IMemberRepository members,
    IUserProvisioning userProvisioning,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<MemberLoginDirectory> logger) : IMemberLoginDirectory
{
    /// <summary>Un documento más corto no sirve de clave provisional.</summary>
    private const int MinDocumentLength = 5;

    public async Task<IReadOnlyList<MemberLoginCandidate>> FindByPhoneAsync(
        string phone, CancellationToken cancellationToken = default)
    {
        // Solo celulares colombianos: los 10 dígitos (3…) con o sin el 57. Un número extranjero entra por correo.
        var e164 = PhoneNumber.TryNormalize(phone);
        if (e164 is not { Length: 13 } || !e164.StartsWith("+573", StringComparison.Ordinal))
        {
            return [];
        }

        var found = await members.GetActiveByPhoneAcrossGymsAsync(e164, cancellationToken);
        return found.Select(m => new MemberLoginCandidate(m.UserId, m.Email)).ToList();
    }

    public async Task<Guid?> ProvisionFirstLoginAsync(
        string email, string document, CancellationToken cancellationToken = default)
    {
        var typed = NormalizeDocument(document);
        if (typed.Length < MinDocumentLength)
        {
            return null;
        }

        var candidates = await members.GetUnlinkedByEmailAcrossGymsAsync(email, cancellationToken);
        var matching = candidates.Where(m => NormalizeDocument(m.DocumentNumber) == typed).ToList();
        if (matching.Count == 0)
        {
            return null;
        }

        var first = matching[0];
        var fullName = $"{first.FirstName} {first.LastName}";
        var accountEmail = first.Email!.Trim();
        var userId = Guid.Empty;

        await unitOfWork.ExecuteInTransactionAsync(async innerToken =>
        {
            userId = await userProvisioning.CreatePendingMemberUserAsync(
                accountEmail, fullName, matching.Select(m => m.GymId).Distinct().ToList(), innerToken);

            foreach (var member in matching)
            {
                member.LinkUser(userId);
                await members.RemovePendingInvitationsAsync(member.Id, innerToken);
            }

            await unitOfWork.SaveChangesAsync(innerToken);
        }, cancellationToken);

        logger.LogInformation(
            "Primer ingreso: la cuenta {UserId} se creó con el documento para {Count} ficha(s)", userId, matching.Count);

        foreach (var member in matching)
        {
            await events.PublishAsync(
                new MemberRegistered(member.Id, userId, member.GymId, accountEmail, $"{member.FirstName} {member.LastName}"),
                cancellationToken);
        }

        return userId;
    }

    public async Task<bool> DocumentMatchesAsync(
        Guid userId, string document, CancellationToken cancellationToken = default)
    {
        var typed = NormalizeDocument(document);
        if (typed.Length < MinDocumentLength)
        {
            return false;
        }

        var linked = await members.GetByUserAcrossGymsAsync(userId, cancellationToken);
        return linked.Any(m => m.Status != MemberStatus.Inactive && NormalizeDocument(m.DocumentNumber) == typed);
    }

    /// <summary>Sin espacios, puntos ni guiones y en mayúsculas: <c>1.020.304.050</c> = <c>1020304050</c>.</summary>
    internal static string NormalizeDocument(string? value)
        => string.Concat((value ?? string.Empty).Where(char.IsLetterOrDigit)).ToUpperInvariant();
}
