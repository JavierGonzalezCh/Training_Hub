using GSM.Application.Attendance.Abstractions;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Public;
using GSM.Domain.Attendance;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Attendance;

internal static class EntryRuleMapping
{
    public static EntryRuleResponse None(Guid memberId)
        => new(memberId, false, false, null, null, null, null, null, null);

    public static async Task<EntryRuleResponse> ToResponseAsync(
        MemberEntryRule rule, DateOnly today, IIdentityReader identity, CancellationToken cancellationToken)
    {
        var grantedBy = await identity.GetUserAsync(rule.GrantedBy, cancellationToken);
        var active = rule.IsActiveOn(today);
        return new EntryRuleResponse(
            rule.MemberId, active, !active, rule.MaxDailyEntries, rule.Reason, rule.ValidUntil,
            rule.GrantedBy, grantedBy?.FullName, rule.GrantedAt);
    }
}

/// <summary>Entradas por día de un miembro (ficha, A1b). Sin regla vigente rige una por día.</summary>
public sealed class GetEntryRuleHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IMemberEntryRuleRepository rules,
    IIdentityReader identity)
{
    public async Task<EntryRuleResponse> HandleAsync(Guid memberId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        if (!await members.ExistsAsync(memberId, cancellationToken))
        {
            throw new NotFoundException("Miembro no encontrado.");
        }

        var rule = await rules.GetForMemberAsync(memberId, cancellationToken);
        return rule is null
            ? EntryRuleMapping.None(memberId)
            : await EntryRuleMapping.ToResponseAsync(rule, clock.Today, identity, cancellationToken);
    }
}

/// <summary>Habilita o cambia las entradas múltiples (Owner o cualquier staff). Queda quién y por qué.</summary>
public sealed class SetEntryRuleHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IMemberEntryRuleRepository rules,
    IIdentityReader identity,
    IUnitOfWork unitOfWork,
    ILogger<SetEntryRuleHandler> logger)
{
    public async Task<EntryRuleResponse> HandleAsync(
        Guid memberId, SetEntryRuleRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var userId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");
        if (!await members.ExistsAsync(memberId, cancellationToken))
        {
            throw new NotFoundException("Miembro no encontrado.");
        }

        var rule = await rules.GetForMemberAsync(memberId, cancellationToken);
        if (rule is null)
        {
            rule = MemberEntryRule.Create(
                gymId, memberId, request.MaxDailyEntries, request.Reason, request.ValidUntil,
                userId, clock.UtcNow, clock.Today);
            rules.Add(rule);
        }
        else
        {
            rule.Apply(request.MaxDailyEntries, request.Reason, request.ValidUntil, userId, clock.UtcNow, clock.Today);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Entradas múltiples del miembro {MemberId}: {Max} por día, hasta {ValidUntil}, por {UserId}",
            memberId, rule.MaxDailyEntries, rule.ValidUntil, userId);

        return await EntryRuleMapping.ToResponseAsync(rule, clock.Today, identity, cancellationToken);
    }
}

/// <summary>Quita las entradas múltiples: el miembro vuelve a una entrada por día. Idempotente.</summary>
public sealed class RemoveEntryRuleHandler(
    ICurrentUserService currentUser,
    IMemberReader members,
    IMemberEntryRuleRepository rules,
    IUnitOfWork unitOfWork,
    ILogger<RemoveEntryRuleHandler> logger)
{
    public async Task HandleAsync(Guid memberId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        if (!await members.ExistsAsync(memberId, cancellationToken))
        {
            throw new NotFoundException("Miembro no encontrado.");
        }

        var rule = await rules.GetForMemberAsync(memberId, cancellationToken);
        if (rule is null)
        {
            return;
        }

        rules.Remove(rule);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Entradas múltiples quitadas al miembro {MemberId} por {UserId}", memberId, currentUser.UserId);
    }
}
