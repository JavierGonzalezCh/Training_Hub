using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Contracts;
using GSM.Application.Memberships.Public;
using GSM.Domain.Members;

namespace GSM.Application.Members;

/// <summary>
/// Lista de miembros con su estado comercial. Memberships calcula el estado (depende del día y no se
/// guarda en la ficha): para filtrar, entrega los ids de cada estado y aquí se acota la consulta.
/// </summary>
public sealed class ListMembersHandler(
    ICurrentUserService currentUser, IMemberRepository members, IMembershipReader memberships,
    IIdentityReader identity)
{
    private static readonly CommercialStatus[] WithPlan =
        [CommercialStatus.Current, CommercialStatus.Expiring, CommercialStatus.Grace,
         CommercialStatus.Expired, CommercialStatus.Frozen];

    public async Task<PagedResult<MemberListItem>> HandleAsync(
        ListMembersQuery query, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        IReadOnlyCollection<Guid>? ids = null;
        var exclude = false;
        if (TryParseStatus(query.CommercialStatus, out var wanted))
        {
            if (wanted == CommercialStatus.NoPlan)
            {
                // Quien no tiene membresías no aparece en Memberships: se pide el complemento.
                ids = await memberships.GetMemberIdsByStatusAsync(WithPlan, cancellationToken);
                exclude = true;
            }
            else
            {
                ids = await memberships.GetMemberIdsByStatusAsync([wanted], cancellationToken);
            }
        }

        var (items, total) = await members.GetPageAsync(
            new MemberPageQuery(query.Search?.Trim(), query.Status, query.Tag?.Trim(), page, pageSize, ids, exclude, query.TrainerUserId, query.WithoutTrainer),
            cancellationToken);

        var statuses = await memberships.GetCommercialStatusesAsync(
            items.Select(m => m.Id).ToList(), cancellationToken);

        var trainerNames = await AuthorNames.ResolveAsync(
            identity, items.Select(m => m.TrainerUserId), cancellationToken);

        return new PagedResult<MemberListItem>(
            items.Select(m => MemberMapper.ToListItem(m) with
            {
                TrainerName = m.TrainerUserId is { } t && trainerNames.TryGetValue(t, out var tn) ? tn : null,
                CommercialStatus = EnumText.Snake(statuses.GetValueOrDefault(m.Id, CommercialStatus.NoPlan))!,
            }).ToList(),
            page, pageSize, total);
    }

    private static bool TryParseStatus(string? text, out CommercialStatus status)
    {
        status = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var candidate in Enum.GetValues<CommercialStatus>())
        {
            if (string.Equals(EnumText.Snake(candidate), text.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                status = candidate;
                return true;
            }
        }

        return false;
    }
}
