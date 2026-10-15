using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Logs;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Logs;

/// <summary>Combina los grupos agregados con el estado guardado, y reabre solos los resueltos que volvieron a ocurrir.</summary>
internal static class ErrorGroupComposer
{
    public static ErrorGroupItem Compose(LogGroupAggregate group, LogGroupState? state)
    {
        var status = state?.Status ?? ErrorGroupStatuses.New;
        var reopened = false;

        // "Un grupo resuelto que vuelve a ocurrir se reabre solo" (§7): se deriva de las fechas, sin escribir.
        if (state is { Status: ErrorGroupStatuses.Resolved } && group.LastSeenAt > state.StatusChangedAt)
        {
            status = ErrorGroupStatuses.New;
            reopened = true;
        }

        return new ErrorGroupItem(
            group.Kind, group.Fingerprint, group.Title, group.Detail, group.Count, group.GymsAffected,
            state?.FirstSeenAt is { } first && first < group.FirstSeenAt ? first : group.FirstSeenAt,
            group.LastSeenAt, status, reopened, state?.Notes, group.PendingRetries);
    }
}

/// <summary>Fallas agrupadas por huella (errores, eventos fallidos, servicios externos, errores del cliente).</summary>
public sealed class ListErrorGroupsHandler(ILogAdminStore store)
{
    public const int LimitPerKind = 500;
    public const int MaxPageSize = 100;

    public async Task<PagedResult<ErrorGroupItem>> HandleAsync(ErrorGroupsQuery query, CancellationToken cancellationToken)
    {
        if (query.Kind is { Length: > 0 } kind && !ErrorGroupKinds.All.Contains(kind))
        {
            throw new ValidationDomainException($"Tipo de grupo no válido: {kind}.");
        }

        if (query.Status is { Length: > 0 } statusFilter && !ErrorGroupStatuses.All.Contains(statusFilter))
        {
            throw new ValidationDomainException($"Estado no válido: {statusFilter}.");
        }

        var aggregates = await store.GetGroupsAsync(
            string.IsNullOrWhiteSpace(query.Kind) ? null : query.Kind, LimitPerKind, cancellationToken);
        var states = (await store.GetGroupStatesAsync(cancellationToken))
            .ToDictionary(s => (s.Kind, s.Fingerprint));

        var items = aggregates
            .Select(g => ErrorGroupComposer.Compose(g, states.GetValueOrDefault((g.Kind, g.Fingerprint))));

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            items = items.Where(i => i.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            items = items.Where(i =>
                i.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (i.Detail?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || i.Fingerprint.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        items = query.Sort switch
        {
            "frequent" => items.OrderByDescending(i => i.Count).ThenByDescending(i => i.LastSeenAt),
            "new" => items.OrderByDescending(i => i.FirstSeenAt),
            _ => items.OrderByDescending(i => i.LastSeenAt),
        };

        var all = items.ToList();
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var page = Math.Max(query.Page, 1);
        return new PagedResult<ErrorGroupItem>(
            all.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, pageSize, all.Count);
    }
}

/// <summary>Un grupo con sus ocurrencias más recientes (código de rastreo, gym, usuario, traza y petición).</summary>
public sealed class GetErrorGroupHandler(ILogAdminStore store)
{
    public const int OccurrenceLimit = 50;

    public async Task<ErrorGroupDetail> HandleAsync(string kind, string fingerprint, CancellationToken cancellationToken)
    {
        if (!ErrorGroupKinds.All.Contains(kind))
        {
            throw new ValidationDomainException($"Tipo de grupo no válido: {kind}.");
        }

        var group = await store.GetGroupAsync(kind, fingerprint, cancellationToken)
            ?? throw new NotFoundException("Grupo no encontrado.");
        var state = await store.GetGroupStateAsync(kind, fingerprint, cancellationToken);
        var occurrences = await store.GetOccurrencesAsync(kind, fingerprint, OccurrenceLimit, cancellationToken);

        return new ErrorGroupDetail(ErrorGroupComposer.Compose(group, state), occurrences);
    }
}

/// <summary>Cambia el estado (y las notas) de un grupo: <c>new</c>, <c>investigating</c>, <c>resolved</c> o <c>ignored</c>.</summary>
public sealed class SetErrorGroupStatusHandler(
    ILogAdminStore store,
    IClock clock,
    ICurrentUserService currentUser)
{
    public const int NotesMaxLength = 4000;

    public async Task<ErrorGroupItem> HandleAsync(SetErrorGroupStatusRequest request, CancellationToken cancellationToken)
    {
        if (!ErrorGroupKinds.All.Contains(request.Kind))
        {
            throw new ValidationDomainException($"Tipo de grupo no válido: {request.Kind}.");
        }

        if (!ErrorGroupStatuses.All.Contains(request.Status))
        {
            throw new ValidationDomainException($"Estado no válido: {request.Status}.");
        }

        if (request.Notes is { Length: > NotesMaxLength })
        {
            throw new ValidationDomainException($"Las notas no pueden pasar de {NotesMaxLength} caracteres.");
        }

        var group = await store.GetGroupAsync(request.Kind, request.Fingerprint, cancellationToken)
            ?? throw new NotFoundException("Grupo no encontrado.");
        var previous = await store.GetGroupStateAsync(request.Kind, request.Fingerprint, cancellationToken);

        var notes = request.Notes is null ? previous?.Notes : request.Notes.Trim();
        var state = new LogGroupState(
            request.Kind, request.Fingerprint, Clip(group.Title, 512),
            previous?.FirstSeenAt ?? group.FirstSeenAt, request.Status,
            string.IsNullOrEmpty(notes) ? null : notes, clock.UtcNow, currentUser.UserId);
        await store.SaveGroupStateAsync(state, cancellationToken);

        return ErrorGroupComposer.Compose(group, state);
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max];
}

/// <summary>
/// Busca por el código que ve el usuario: el prefijo del <c>traceId</c> (6 caracteres, sin distinguir mayúsculas) y
/// devuelve los registros de cualquier tabla de logs. Puede traer más de una petición: se distinguen por fecha y gym.
/// </summary>
public sealed class SearchTraceHandler(ILogAdminStore store)
{
    public const int MinPrefix = 4;
    public const int Limit = 200;

    public async Task<TraceSearchResponse> HandleAsync(string code, CancellationToken cancellationToken)
    {
        var prefix = (code ?? string.Empty).Trim().ToLowerInvariant();
        if (prefix.Length < MinPrefix || prefix.Length > 32 || !prefix.All(Uri.IsHexDigit))
        {
            throw new ValidationDomainException(
                $"El código de rastreo son al menos {MinPrefix} caracteres hexadecimales (por ejemplo 7F3A2C).");
        }

        var records = await store.SearchTraceAsync(prefix, Limit, cancellationToken);
        return new TraceSearchResponse(prefix.ToUpperInvariant(), records.OrderByDescending(r => r.OccurredAt).ToList());
    }
}
