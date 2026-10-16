using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Logs;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// LG4-4: el reintento de un evento fallido lo pide un SuperAdmin, pero el manejador corre en el gym del evento
/// (<see cref="GymExecutionScope"/>), con el filtro de tenant puesto, igual que cuando se publicó.
/// </summary>
public sealed class EventRetryGymScopeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));

    [Fact]
    public async Task A_retry_requested_by_the_superadmin_runs_the_handler_inside_the_gym_of_the_event()
    {
        var gymA = await SeedGymAsync();
        var gymB = await SeedGymAsync();
        var probe = new ProbeLog();

        await using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.AddSingleton(probe);
            services.AddScoped<IDomainEventHandler<RetryProbeEvent>, RetryProbeHandler>();
        }));

        using var scope = host.Services.CreateScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<MutableCurrentUser>();
        currentUser.ActAs(Guid.CreateVersion7(), null, UserRole.SuperAdmin);

        var failure = new EventFailureRecord(
            Guid.CreateVersion7(), DateTimeOffset.UtcNow, null, gymA,
            typeof(RetryProbeEvent).FullName!, typeof(RetryProbeHandler).FullName!,
            $$"""{"gymId":"{{gymA}}"}""", "InvalidOperationException", "x", EventRetryStatuses.Pending, null, null);

        var result = await scope.ServiceProvider.GetRequiredService<EventFailureReplayer>().ReplayAsync(failure, default);

        Assert.Equal(EventRetryOutcomes.Succeeded, result.Outcome);
        Assert.Equal(gymA, probe.SeenGymId);
        Assert.False(probe.SeenSuperAdmin);
        // Con el filtro puesto solo ve los miembros del gym A, no los del B.
        Assert.Equal([gymA], probe.MemberGyms);

        // Al salir del reintento se recupera la identidad del SuperAdmin.
        Assert.True(currentUser.IsSuperAdmin);
        Assert.Null(currentUser.GymId);
        Assert.NotEqual(gymA, gymB);
    }

    private async Task<Guid> SeedGymAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Retry Gym", $"rt-{Guid.NewGuid():N}"[..16]);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
        db.Add(Member.Create(gym.Id, "Ana", "Retry", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-30)));
        await db.SaveChangesAsync();
        return gym.Id;
    }
}

public sealed record RetryProbeEvent(Guid GymId) : IDomainEvent;

public sealed class ProbeLog
{
    public Guid? SeenGymId { get; set; }

    public bool SeenSuperAdmin { get; set; }

    public List<Guid> MemberGyms { get; set; } = [];
}

public sealed class RetryProbeHandler(ICurrentUserService currentUser, GsmDbContext db, ProbeLog log)
    : IDomainEventHandler<RetryProbeEvent>
{
    public async Task HandleAsync(RetryProbeEvent domainEvent, CancellationToken cancellationToken)
    {
        log.SeenGymId = currentUser.GymId;
        log.SeenSuperAdmin = currentUser.IsSuperAdmin;
        log.MemberGyms = await db.Set<Member>().Select(m => m.GymId).Distinct().ToListAsync(cancellationToken);
    }
}
