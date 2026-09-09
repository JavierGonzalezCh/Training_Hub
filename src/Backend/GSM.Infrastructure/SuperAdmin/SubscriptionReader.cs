using GSM.Application.Common.Abstractions;
using GSM.Application.SuperAdmin.Public;
using GSM.Domain.Gyms;
using GSM.Domain.SuperAdmin;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.SuperAdmin;

/// <summary>
/// Estado comercial efectivo por id explícito, ignorando el Global Query Filter. Un gym sin
/// suscripción devuelve "sin dato" (no se bloquea): dejar tirado a un cliente por un registro
/// que falta es peor error que no cortarle a un moroso.
/// </summary>
public sealed class SubscriptionReader(GsmDbContext dbContext, IClock clock) : ISubscriptionReader
{
    // Caché por petición: el reader es scoped, así que una sola consulta por gym y request,
    // aunque el filtro de bloqueo y el bloque `subscription` de la respuesta lo pidan por separado.
    private readonly Dictionary<Guid, SubscriptionStatusView?> _cache = new();

    public async Task<EffectiveSubscriptionStatus?> GetEffectiveStatusAsync(
        Guid gymId, CancellationToken cancellationToken = default)
        => (await LoadAsync(gymId, cancellationToken))?.Status;

    public async Task<bool> IsWriteAllowedAsync(Guid gymId, CancellationToken cancellationToken = default)
    {
        var view = await LoadAsync(gymId, cancellationToken);
        return view is null
               || view.Status is not (EffectiveSubscriptionStatus.Disabled or EffectiveSubscriptionStatus.Suspended);
    }

    public async Task<SubscriptionStatusView?> GetStatusViewAsync(
        Guid gymId, CancellationToken cancellationToken = default)
        => await LoadAsync(gymId, cancellationToken);

    private async Task<SubscriptionStatusView?> LoadAsync(Guid gymId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(gymId, out var cached))
        {
            return cached;
        }

        var row = await (
                from s in dbContext.Set<GymSubscription>().AsNoTracking()
                join g in dbContext.Set<Gym>().IgnoreQueryFilters() on s.GymId equals g.Id
                where s.GymId == gymId
                select new { g.Status, s.PaidThrough, s.TrialEndsOn })
            .FirstOrDefaultAsync(cancellationToken);

        SubscriptionStatusView? view = null;
        if (row is not null)
        {
            var today = clock.Today;
            view = new SubscriptionStatusView(
                GymSubscription.DeriveStatus(row.Status, today, row.PaidThrough, row.TrialEndsOn),
                row.PaidThrough,
                GymSubscription.DaysUntilDisabled(row.Status, today, row.PaidThrough, row.TrialEndsOn));
        }

        _cache[gymId] = view;
        return view;
    }
}
