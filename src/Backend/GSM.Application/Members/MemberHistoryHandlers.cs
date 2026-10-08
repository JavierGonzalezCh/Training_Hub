using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Members;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Members;

/// <summary>Resuelve los nombres de los autores (cuentas de Identity) por <see cref="IIdentityReader"/>.</summary>
internal static class AuthorNames
{
    public static async Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(
        IIdentityReader identity, IEnumerable<Guid?> userIds, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var id in userIds.Where(i => i.HasValue).Select(i => i!.Value).Distinct())
        {
            if (await identity.GetUserAsync(id, cancellationToken) is { } user)
            {
                names[id] = user.FullName;
            }
        }

        return names;
    }
}

/// <summary>Historial de cambios de la ficha, paginado, del más reciente al más antiguo.</summary>
public sealed class ListMemberChangesHandler(
    ICurrentUserService currentUser,
    IMemberRepository members,
    IIdentityReader identity)
{
    public async Task<PagedResult<MemberChangeItem>> HandleAsync(
        Guid memberId, ListMemberChangesQuery query, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        if (!await members.ExistsAsync(memberId, cancellationToken))
        {
            throw new NotFoundException("Miembro no encontrado.");
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var (items, total) = await members.GetChangesPageAsync(memberId, page, pageSize, cancellationToken);
        var names = await AuthorNames.ResolveAsync(identity, items.Select(c => c.ChangedByUserId), cancellationToken);

        return new PagedResult<MemberChangeItem>(
            items.Select(c => new MemberChangeItem(
                c.Id,
                c.Field,
                c.OldValue,
                c.NewValue,
                c.ChangedByUserId,
                c.ChangedByUserId is { } id && names.TryGetValue(id, out var name) ? name : null,
                EnumText.Snake(c.ChangedByRole)!,
                EnumText.Snake(c.Source)!,
                c.ChangedAt,
                c.IsSensitive)).ToList(),
            page,
            pageSize,
            total);
    }
}

/// <summary>Notas internas del miembro. Solo staff: el controller exige la política de staff.</summary>
public sealed class ListMemberNotesHandler(
    ICurrentUserService currentUser,
    IMemberRepository members,
    IIdentityReader identity)
{
    public async Task<IReadOnlyList<MemberNoteItem>> HandleAsync(Guid memberId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        if (!await members.ExistsAsync(memberId, cancellationToken))
        {
            throw new NotFoundException("Miembro no encontrado.");
        }

        var notes = await members.GetNotesAsync(memberId, cancellationToken);
        var names = await AuthorNames.ResolveAsync(identity, notes.Select(n => n.AuthorUserId), cancellationToken);
        return notes.Select(n => ToItem(n, names)).ToList();
    }

    internal static MemberNoteItem ToItem(MemberNote n, IReadOnlyDictionary<Guid, string> names)
        => new(
            n.Id,
            n.Text,
            n.AuthorUserId,
            n.AuthorUserId is { } id && names.TryGetValue(id, out var name) ? name : null,
            n.CreatedAt);
}

public sealed class AddMemberNoteHandler(
    ICurrentUserService currentUser,
    IMemberRepository members,
    IIdentityReader identity,
    IUnitOfWork unitOfWork,
    ILogger<AddMemberNoteHandler> logger)
{
    public async Task<MemberNoteItem> HandleAsync(
        Guid memberId, AddMemberNoteRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        if (!await members.ExistsAsync(memberId, cancellationToken))
        {
            throw new NotFoundException("Miembro no encontrado.");
        }

        var note = MemberNote.Add(gymId, memberId, request.Text, currentUser.UserId);
        members.Add(note);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Nota interna añadida al miembro {MemberId}", memberId);

        var names = await AuthorNames.ResolveAsync(identity, [note.AuthorUserId], cancellationToken);
        return ListMemberNotesHandler.ToItem(note, names);
    }
}
