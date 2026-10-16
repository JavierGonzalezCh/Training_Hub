using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms;
using GSM.Application.Gyms.Public;
using GSM.Domain.Gyms;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Gyms;

/// <summary>
/// Lectura cruzada por id explícito: ignora el Global Query Filter a propósito para que el
/// contrato sea predecible sin importar el contexto del llamador.
/// </summary>
public sealed class GymReader(GsmDbContext dbContext, IClock clock) : IGymReader
{
    public Task<bool> ExistsAsync(Guid gymId, CancellationToken cancellationToken = default)
        => dbContext.Set<Gym>().IgnoreQueryFilters()
            .AnyAsync(g => g.Id == gymId, cancellationToken);

    public Task<GymBranding?> GetBrandingAsync(Guid gymId, CancellationToken cancellationToken = default)
        => dbContext.Set<Gym>().IgnoreQueryFilters()
            .Where(g => g.Id == gymId)
            .Select(g => new GymBranding(g.Name, g.LogoUrl, g.PrimaryColor, g.SecondaryColor))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<GymBranding?> GetBrandingBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        return dbContext.Set<Gym>().IgnoreQueryFilters()
            .Where(g => g.Slug == normalized)
            .Select(g => new GymBranding(g.Name, g.LogoUrl, g.PrimaryColor, g.SecondaryColor))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<int?> GetCapacityAsync(Guid gymId, CancellationToken cancellationToken = default)
        => dbContext.Set<Gym>().IgnoreQueryFilters()
            .Where(g => g.Id == gymId)
            .Select(g => g.Capacity)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<GymGeofence?> GetGeofenceAsync(Guid gymId, CancellationToken cancellationToken = default)
        => dbContext.Set<Gym>().IgnoreQueryFilters()
            .Where(g => g.Id == gymId && g.Latitude != null && g.Longitude != null)
            .Select(g => new GymGeofence(g.Latitude!.Value, g.Longitude!.Value, g.GeofenceRadiusMeters))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<GymFeatureFlagsView?> GetFeatureFlagsAsync(Guid gymId, CancellationToken cancellationToken = default)
        => dbContext.Set<GymFeatureFlags>().IgnoreQueryFilters()
            .Where(f => f.GymId == gymId)
            .Select(f => new GymFeatureFlagsView(
                f.ShowRoutines, f.ShowClasses, f.ShowProgress, f.ShowGamification,
                f.ShowCommunity, f.CheckinQr, f.CommunityComments, f.MembersCanCreateRoutines,
                f.MembersCanReportEquipment))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<GymAdminView?> GetAdminViewAsync(Guid gymId, CancellationToken cancellationToken = default)
        => dbContext.Set<Gym>().IgnoreQueryFilters()
            .Where(g => g.Id == gymId)
            .Select(g => new GymAdminView(g.Id, g.Name, g.Slug, g.Nit, g.Phone, g.Email, g.Status))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<GymOpenStatus> GetOpenStatusAsync(
        Guid gymId, DateTimeOffset instant, CancellationToken cancellationToken = default)
    {
        var local = clock.ToBusinessLocal(instant);
        return GetOpenStatusAtLocalAsync(gymId, DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local), cancellationToken);
    }

    public async Task<GymOpenStatus> GetOpenStatusAtLocalAsync(
        Guid gymId, DateOnly localDate, TimeOnly localTime, CancellationToken cancellationToken = default)
    {
        var weekly = await dbContext.Set<GymOpeningSlot>().IgnoreQueryFilters()
            .Where(s => s.GymId == gymId)
            .ToListAsync(cancellationToken);
        if (weekly.Count == 0)
        {
            return GymOpenStatus.NoSchedule;
        }

        var specials = await dbContext.Set<GymSpecialDay>().IgnoreQueryFilters()
            .Include(d => d.Slots)
            .Where(d => d.GymId == gymId && d.Date >= localDate && d.Date <= localDate.AddDays(OpeningSchedule.LookAheadDays))
            .ToListAsync(cancellationToken);

        return OpeningHoursSupport.BuildSchedule(weekly, specials).Evaluate(localDate, localTime);
    }

    public async Task<OpeningSchedule> GetOpeningScheduleAsync(
        Guid gymId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var weekly = await dbContext.Set<GymOpeningSlot>().IgnoreQueryFilters()
            .Where(s => s.GymId == gymId)
            .ToListAsync(cancellationToken);

        var specials = await dbContext.Set<GymSpecialDay>().IgnoreQueryFilters()
            .Include(d => d.Slots)
            .Where(d => d.GymId == gymId && d.Date >= from && d.Date <= to)
            .ToListAsync(cancellationToken);

        return OpeningHoursSupport.BuildSchedule(weekly, specials);
    }

    public async Task<bool> IsOpenAtAsync(Guid gymId, DateTimeOffset instant, CancellationToken cancellationToken = default)
        => (await GetOpenStatusAsync(gymId, instant, cancellationToken)).IsOpen;
}
