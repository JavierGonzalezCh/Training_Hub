using GSM.Application.Memberships.Abstractions;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Domain.SuperAdmin;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.SuperAdmin;

/// <summary>Planes del SaaS y límite efectivo de cada gym. Por encima del multi-tenancy.</summary>
public sealed class SaasPlanRepository(GsmDbContext dbContext) : ISaasPlanRepository
{
    private DbSet<SaasPlan> Plans => dbContext.Set<SaasPlan>();

    public void Add(SaasPlan plan) => Plans.Add(plan);

    public Task<SaasPlan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Plans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SaasPlan>> ListAsync(CancellationToken cancellationToken = default)
        => await Plans.AsNoTracking()
            .OrderBy(p => p.SuggestedPrice).ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);

    public Task<SaasPlan?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var lower = name.Trim().ToLower();
        return Plans.FirstOrDefaultAsync(p => p.Name.ToLower() == lower, cancellationToken);
    }

    public Task<bool> NameExistsAsync(string name, Guid? excludingId, CancellationToken cancellationToken = default)
    {
        var lower = name.Trim().ToLower();
        return Plans.AnyAsync(p => p.Name.ToLower() == lower && p.Id != excludingId, cancellationToken);
    }

    public async Task<GymMemberLimit?> GetGymLimitAsync(Guid gymId, CancellationToken cancellationToken = default)
    {
        var sub = await dbContext.Set<GymSubscription>().AsNoTracking()
            .Where(s => s.GymId == gymId)
            .Select(s => new { s.SaasPlanId, s.Plan, s.MemberLimitOverride })
            .FirstOrDefaultAsync(cancellationToken);
        if (sub is null)
        {
            return null;
        }

        SaasPlan? plan;
        if (sub.SaasPlanId is { } planId)
        {
            plan = await Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        }
        else
        {
            // Suscripción sin plan elegido: el nombre del enum heredado ("Basic", "Pro", "Enterprise").
            var legacyName = sub.Plan.ToString().ToLower();
            plan = await Plans.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Name.ToLower() == legacyName, cancellationToken);
        }

        return new GymMemberLimit(plan?.Id, plan?.Name ?? sub.Plan.ToString(), plan?.MemberLimit, sub.MemberLimitOverride);
    }
}

/// <summary>
/// SuperAdmin implementa el puerto de Memberships con el límite efectivo del gym. Un gym sin suscripción o
/// con plan sin límite devuelve <c>null</c> (no se bloquea).
/// </summary>
public sealed class SubscriptionMemberLimitProvider(ISaasPlanRepository plans) : IMemberLimitProvider
{
    public async Task<int?> GetLimitAsync(Guid gymId, CancellationToken cancellationToken = default)
        => (await plans.GetGymLimitAsync(gymId, cancellationToken))?.Effective;
}
