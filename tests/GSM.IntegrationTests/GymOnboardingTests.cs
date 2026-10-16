using GSM.Application.Equipment;
using GSM.Application.Equipment.Contracts;
using GSM.Application.Gyms;
using GSM.Application.Gyms.Contracts;
using GSM.Application.Memberships;
using GSM.Application.Memberships.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Equipment;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>G5: onboarding obligatorio del gym (Gyms §5). El estado se deriva del dato real de cada módulo.</summary>
public sealed class GymOnboardingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_new_gym_starts_pending_and_points_at_the_first_missing_required_step()
    {
        var gymId = await SeedGymAsync();

        var state = await GetAsync(gymId);

        Assert.False(state.IsComplete);
        Assert.False(state.WasSkipped);
        Assert.Equal(7, state.Steps.Count);
        Assert.Equal(GetOnboardingHandler.StepLocation, state.NextStep);
        Assert.True(state.Steps.Single(s => s.Key == GetOnboardingHandler.StepGym).Done);
        Assert.All(state.Steps.Where(s => s.Order is >= 2 and <= 5), s => Assert.False(s.Done));
        Assert.All(state.Steps.Where(s => s.Order <= 5), s => Assert.True(s.Required));
        Assert.All(state.Steps.Where(s => s.Order > 5), s => Assert.False(s.Required));
    }

    [Fact]
    public async Task Each_step_is_done_when_its_data_exists_and_the_gym_completes_once_the_five_are_met()
    {
        var gymId = await SeedGymAsync();

        await SetLocationAsync(gymId, "Calle 10 # 5-20", "Medellín");
        Assert.Equal(GetOnboardingHandler.StepHours, (await GetAsync(gymId)).NextStep);

        using (var scope = Owner(gymId))
        {
            await scope.ServiceProvider.GetRequiredService<UpdateOpeningHoursHandler>().HandleAsync(Week(), default);
        }

        Assert.Equal(GetOnboardingHandler.StepEquipment, (await GetAsync(gymId)).NextStep);

        using (var scope = Owner(gymId))
        {
            await scope.ServiceProvider.GetRequiredService<CreateEquipmentHandler>().HandleAsync(
                new SaveEquipmentRequest("Banco plano", EquipmentType.Machine, "Peso libre", null, null, null, 1, null, null),
                default);
        }

        Assert.Equal(GetOnboardingHandler.StepPlans, (await GetAsync(gymId)).NextStep);

        using (var scope = Owner(gymId))
        {
            await scope.ServiceProvider.GetRequiredService<CreatePlanHandler>().HandleAsync(
                new CreatePlanRequest("Mensual", null, 90000m, 1, "months"), default);
        }

        var done = await GetAsync(gymId);
        Assert.True(done.IsComplete);
        Assert.Null(done.NextStep);
        Assert.NotNull(done.CompletedAt);
        Assert.False(done.WasSkipped);

        // Lo opcional queda en la lista "Para completar".
        Assert.Equal(
            [GetOnboardingHandler.StepEntry, GetOnboardingHandler.StepTeam, GetOnboardingHandler.TodoMembers],
            done.ToComplete.Select(t => t.Key).ToArray());
    }

    [Fact]
    public async Task Completion_is_sticky_even_if_a_required_step_is_undone_later()
    {
        var gymId = await SeedGymAsync();
        await SetLocationAsync(gymId, "Cra 1 # 2-3", "Cali");
        using (var scope = Owner(gymId))
        {
            await scope.ServiceProvider.GetRequiredService<UpdateOpeningHoursHandler>().HandleAsync(Week(), default);
            await scope.ServiceProvider.GetRequiredService<CreateEquipmentHandler>().HandleAsync(
                new SaveEquipmentRequest("Prensa", EquipmentType.Machine, null, null, null, null, 1, null, null), default);
            await scope.ServiceProvider.GetRequiredService<CreatePlanHandler>().HandleAsync(
                new CreatePlanRequest("Anual", null, 800000m, 12, "months"), default);
        }

        Assert.True((await GetAsync(gymId)).IsComplete);

        // El Owner borra la dirección: el gym no vuelve a quedar bloqueado.
        await SetLocationAsync(gymId, null, null);

        var state = await GetAsync(gymId);
        Assert.True(state.IsComplete);
        Assert.False(state.Steps.Single(s => s.Key == GetOnboardingHandler.StepLocation).Done);
    }

    [Fact]
    public async Task SuperAdmin_can_skip_with_a_reason_and_it_is_recorded()
    {
        var gymId = await SeedGymAsync();
        var superAdmin = Guid.CreateVersion7();

        using (var scope = SuperAdmin(superAdmin))
        {
            var skip = scope.ServiceProvider.GetRequiredService<SkipOnboardingHandler>();

            await Assert.ThrowsAsync<ValidationDomainException>(
                () => skip.HandleAsync(gymId, new SkipOnboardingRequest("  "), default));

            var skipped = await skip.HandleAsync(gymId, new SkipOnboardingRequest("Migración de datos hecha por el equipo"), default);
            Assert.True(skipped.IsComplete);
            Assert.True(skipped.WasSkipped);
            Assert.Equal("Migración de datos hecha por el equipo", skipped.SkipReason);

            // Omitir dos veces es un conflicto.
            await Assert.ThrowsAsync<ConflictException>(
                () => skip.HandleAsync(gymId, new SkipOnboardingRequest("otra vez"), default));
        }

        using var system = System();
        var row = await system.ServiceProvider.GetRequiredService<GsmDbContext>().Set<GymOnboarding>()
            .SingleAsync(o => o.GymId == gymId);
        Assert.Equal(superAdmin, row.SkippedBy);
        Assert.NotNull(row.SkippedAt);
        Assert.NotNull(row.CompletedAt);
    }

    [Fact]
    public async Task Skipping_a_gym_that_does_not_exist_is_a_404()
    {
        using var scope = SuperAdmin(Guid.CreateVersion7());
        await Assert.ThrowsAsync<NotFoundException>(() => scope.ServiceProvider.GetRequiredService<SkipOnboardingHandler>()
            .HandleAsync(Guid.CreateVersion7(), new SkipOnboardingRequest("x"), default));
    }

    [Fact]
    public async Task Staff_and_owner_read_only_their_own_gym()
    {
        var a = await SeedGymAsync();
        var b = await SeedGymAsync();
        await SetLocationAsync(b, "Calle 1", "Bogotá");

        using var scopeA = Staff(a);
        var stateA = await scopeA.ServiceProvider.GetRequiredService<GetOnboardingHandler>().HandleAsync(default);
        Assert.Equal(GetOnboardingHandler.StepLocation, stateA.NextStep);

        using var scopeB = Staff(b);
        var stateB = await scopeB.ServiceProvider.GetRequiredService<GetOnboardingHandler>().HandleAsync(default);
        Assert.Equal(GetOnboardingHandler.StepHours, stateB.NextStep);
    }

    // ---- soporte ----

    private async Task<OnboardingResponse> GetAsync(Guid gymId)
    {
        using var scope = Owner(gymId);
        return await scope.ServiceProvider.GetRequiredService<GetOnboardingHandler>().HandleAsync(default);
    }

    private async Task SetLocationAsync(Guid gymId, string? address, string? city)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var gym = await db.Set<Gym>().SingleAsync(g => g.Id == gymId);
        gym.UpdateLocation(address, city, null, null);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedGymAsync()
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var gym = Gym.Create("Onboarding Gym", $"ob-{Guid.NewGuid():N}"[..20]);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();
        return gym.Id;
    }

    private static UpdateOpeningHoursRequest Week()
    {
        OpeningDayDto D(string day, string from, string to) => new(day, [new TimeSlotDto(from, to)]);
        return new UpdateOpeningHoursRequest(
        [
            D("Monday", "05:00", "22:00"), D("Tuesday", "05:00", "22:00"), D("Wednesday", "05:00", "22:00"),
            D("Thursday", "05:00", "22:00"), D("Friday", "05:00", "22:00"), D("Saturday", "07:00", "14:00"),
            D("Sunday", "07:00", "13:00"), D("Holiday", "07:00", "13:00"),
        ]);
    }

    private IServiceScope System()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }

    private IServiceScope Owner(Guid gymId)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), gymId, UserRole.Owner);
        return scope;
    }

    private IServiceScope Staff(Guid gymId)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), gymId, UserRole.Staff);
        return scope;
    }

    private IServiceScope SuperAdmin(Guid userId)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(userId, gymId: null, UserRole.SuperAdmin);
        return scope;
    }
}
