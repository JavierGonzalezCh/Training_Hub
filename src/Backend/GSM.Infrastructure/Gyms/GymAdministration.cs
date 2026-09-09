using GSM.Application.Gyms.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Gyms;

/// <summary>
/// Escritura acotada para SuperAdmin. Rastrea el <see cref="Gym"/> en el <see cref="GsmDbContext"/>
/// compartido; el <c>SaveChanges</c> lo dispara el caso de uso que la invoca, en su transacción.
/// </summary>
public sealed class GymAdministration(GsmDbContext dbContext) : IGymAdministration
{
    public async Task SetCommercialStatusAsync(
        Guid gymId, GymStatus status, CancellationToken cancellationToken = default)
    {
        var gym = await dbContext.Set<Gym>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.Id == gymId, cancellationToken)
            ?? throw new NotFoundException("El gym no existe.");

        gym.SetCommercialStatus(status);
    }
}
