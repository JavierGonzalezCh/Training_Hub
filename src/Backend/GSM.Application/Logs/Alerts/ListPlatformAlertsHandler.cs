using GSM.Application.Common;

namespace GSM.Application.Logs.Alerts;

/// <summary>Historial de las alertas emitidas al SuperAdmin, las más recientes primero.</summary>
public sealed class ListPlatformAlertsHandler(IPlatformAlertStore store)
{
    public const int MaxPageSize = 100;

    public async Task<PagedResult<PlatformAlertItem>> HandleAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var (items, total) = await store.ListAsync(page, pageSize, cancellationToken);
        return new PagedResult<PlatformAlertItem>(
            items.Select(a => new PlatformAlertItem(
                a.Id, a.CreatedAt, a.Kind, a.Title, a.Body, a.Link, a.EmailStatus, a.PushStatus)).ToList(),
            page, pageSize, total);
    }
}
