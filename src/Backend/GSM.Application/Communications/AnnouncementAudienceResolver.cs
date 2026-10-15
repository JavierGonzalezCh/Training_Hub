using GSM.Application.Classes.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Communications.Contracts;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Communications;

namespace GSM.Application.Communications;

/// <summary>
/// Calcula a quién llega un anuncio (CO2). Parte de los miembros activos del gym en contexto (los dados
/// de baja nunca están) y aplica cada filtro con "y"; dentro de un filtro los valores suman. Lee solo
/// por los <c>I{Módulo}Reader</c> de Members, Memberships y Classes.
/// </summary>
public sealed class AnnouncementAudienceResolver(
    IMemberReader members,
    IMembershipReader memberships,
    IClassReader classes,
    IClock clock)
{
    /// <summary>Ventana del filtro por clase: reserva o asistencia en los últimos N días.</summary>
    public const int ClassLookbackDays = 30;

    public async Task<IReadOnlyList<Guid>> ResolveAsync(
        AnnouncementAudience audience, Guid gymId, CancellationToken cancellationToken)
    {
        var current = (await members.GetActiveMemberIdsAsync(gymId, cancellationToken)).ToHashSet();
        if (audience.IsEveryone || current.Count == 0)
        {
            return current.ToList();
        }

        if (audience.Statuses.Count > 0)
        {
            var wanted = audience.Statuses.Select(ParseStatus).ToHashSet();
            var statuses = await memberships.GetCommercialStatusesAsync(current, cancellationToken);
            current.RemoveWhere(id => !wanted.Contains(statuses.GetValueOrDefault(id, CommercialStatus.NoPlan)));
        }

        if (audience.PlanIds.Count > 0 && current.Count > 0)
        {
            current.IntersectWith(await memberships.GetMemberIdsByPlansAsync(audience.PlanIds, cancellationToken));
        }

        if (audience.ClassIds.Count > 0 && current.Count > 0)
        {
            var since = clock.Today.AddDays(-ClassLookbackDays);
            current.IntersectWith(await classes.GetMemberIdsByClassesAsync(audience.ClassIds, since, cancellationToken));
        }

        if (audience.Tags.Count > 0 && current.Count > 0)
        {
            current.IntersectWith(await members.GetActiveMemberIdsByTagsAsync(audience.Tags, cancellationToken));
        }

        return current.ToList();
    }

    public static CommercialStatus ParseStatus(string value)
        => Enum.TryParse<CommercialStatus>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ValidationDomainException(
                "Estado comercial inválido: Current, Expiring, Grace, Expired, Frozen o NoPlan.");
}

internal static class AnnouncementAudienceMapper
{
    /// <summary>Valida los estados y normaliza los filtros. <c>null</c> = todos.</summary>
    public static AnnouncementAudience ToDomain(AnnouncementAudienceDto? dto)
    {
        if (dto is null)
        {
            return AnnouncementAudience.Everyone;
        }

        var statuses = (dto.Statuses ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => AnnouncementAudienceResolver.ParseStatus(s.Trim()).ToString())
            .ToList();
        return AnnouncementAudience.Create(statuses, dto.PlanIds, dto.ClassIds, dto.Tags);
    }

    public static AnnouncementAudienceDto ToDto(AnnouncementAudience audience)
        => new(audience.Statuses.ToList(), audience.PlanIds.ToList(), audience.ClassIds.ToList(), audience.Tags.ToList());
}
