using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Contracts;

namespace GSM.Application.Members;

public sealed class ListMembersHandler(ICurrentUserService currentUser, IMemberRepository members)
{
    public async Task<PagedResult<MemberListItem>> HandleAsync(
        ListMembersQuery query, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var (items, total) = await members.GetPageAsync(
            new MemberPageQuery(query.Search?.Trim(), query.Status, query.Tag?.Trim(), page, pageSize), cancellationToken);

        return new PagedResult<MemberListItem>(
            items.Select(MemberMapper.ToListItem).ToList(), page, pageSize, total);
    }
}
