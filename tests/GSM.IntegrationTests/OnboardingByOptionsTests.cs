using GSM.Application.Gyms;
using GSM.Application.Gyms.Contracts;
using GSM.Application.Memberships;
using GSM.Application.Memberships.Contracts;
using GSM.Domain.Attendance;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// T5 — los pasos obligatorios del onboarding salen de las <b>opciones</b> del gym (entrenamiento fuera de la sede,
/// rutinas, reporte de fallas), no de su tipo; y <c>ShowFrontDesk</c> es solo una opción de interfaz.
/// </summary>
public sealed class OnboardingByOptionsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    // remoto, rutinas, reporte de fallas -> ubicación, horario, equipos obligatorios
    [InlineData(false, true, true, true, true, true)]
    [InlineData(false, false, false, true, true, false)]   // sin rutinas ni reportes no hay equipos que pedir
    [InlineData(false, false, true, true, true, true)]
    [InlineData(true, true, true, false, false, false)]    // entrena fuera: nada de sede
    [InlineData(true, false, false, false, false, false)]
    public async Task Location_hours_and_equipment_are_required_only_when_the_options_need_a_site(
        bool remote, bool routines, bool reportEquipment, bool location, bool hours, bool equipment)
    {
        var gymId = await SeedGymAsync(remote, routines, reportEquipment);

        var state = await GetAsync(gymId);

        Assert.Equal(7, state.Steps.Count);
        Assert.True(Required(state, GetOnboardingHandler.StepGym));
        Assert.Equal(location, Required(state, GetOnboardingHandler.StepLocation));
        Assert.Equal(hours, Required(state, GetOnboardingHandler.StepHours));
        Assert.Equal(equipment, Required(state, GetOnboardingHandler.StepEquipment));
        Assert.True(Required(state, GetOnboardingHandler.StepPlans));
        Assert.False(Required(state, GetOnboardingHandler.StepEntry));
        Assert.False(Required(state, GetOnboardingHandler.StepTeam));
    }

    [Fact]
    public async Task A_gym_that_trains_remotely_is_enabled_with_only_its_data_and_a_plan()
    {
        var gymId = await SeedGymAsync(remote: true, routines: true, reportEquipment: true);

        var before = await GetAsync(gymId);
        Assert.False(before.IsComplete);
        Assert.Equal(GetOnboardingHandler.StepPlans, before.NextStep);

        using (var scope = Owner(gymId))
        {
            await scope.ServiceProvider.GetRequiredService<CreatePlanHandler>().HandleAsync(
                new CreatePlanRequest("Mensual", null, 150000m, 1, "months"), default);
        }

        var after = await GetAsync(gymId);
        Assert.True(after.IsComplete);
        Assert.Null(after.NextStep);
        // Los pasos que dejaron de ser obligatorios siguen ahí, sin hacer y como opcionales.
        Assert.False(after.Steps.Single(s => s.Key == GetOnboardingHandler.StepLocation).Done);
        Assert.False(Required(after, GetOnboardingHandler.StepLocation));
    }

    [Fact]
    public async Task Without_the_remote_option_nothing_changes_and_the_five_steps_stay_required()
    {
        var gymId = await SeedGymAsync(remote: false, routines: true, reportEquipment: true);

        var state = await GetAsync(gymId);

        Assert.All(state.Steps.Where(s => s.Order <= 5), s => Assert.True(s.Required));
        Assert.Equal(GetOnboardingHandler.StepLocation, state.NextStep);
    }

    // ---- ShowFrontDesk ----

    [Fact]
    public void Front_desk_is_on_for_every_type_except_the_personal_trainer()
    {
        foreach (var type in Enum.GetValues<GymType>())
        {
            var flags = GymFeatureFlags.ForType(Guid.CreateVersion7(), type);
            Assert.Equal(type != GymType.PersonalTrainer, flags.ShowFrontDesk);
        }

        Assert.True(GymFeatureFlags.Default(Guid.CreateVersion7()).ShowFrontDesk);
    }

    [Fact]
    public async Task The_owner_turns_front_desk_off_and_older_clients_that_omit_it_keep_it()
    {
        var gymId = await SeedGymAsync(remote: false, routines: true, reportEquipment: true);

        UpdateFeatureFlagsRequest Request(bool? frontDesk) => new(
            ShowRoutines: true, ShowClasses: true, ShowProgress: true, ShowGamification: true, ShowCommunity: true,
            CheckinQr: true, MembersCanCreateRoutines: false, MembersCanReportEquipment: true, ShowFrontDesk: frontDesk);

        using (var scope = Owner(gymId))
        {
            var update = scope.ServiceProvider.GetRequiredService<UpdateFeatureFlagsHandler>();
            Assert.False((await update.HandleAsync(Request(false), default)).ShowFrontDesk);
            Assert.False((await update.HandleAsync(Request(null), default)).ShowFrontDesk);
            Assert.True((await update.HandleAsync(Request(true), default)).ShowFrontDesk);
        }
    }

    [Fact]
    public async Task Front_desk_off_blocks_nothing_in_the_api()
    {
        var gymId = await SeedGymAsync(remote: false, routines: true, reportEquipment: true);
        using (var scope = Owner(gymId))
        {
            var flags = scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<GymFeatureFlags>();
            var row = await flags.SingleAsync(f => f.GymId == gymId);
            row.Update(true, true, true, true, true, true, false, true, showFrontDesk: false);
            await scope.ServiceProvider.GetRequiredService<GsmDbContext>().SaveChangesAsync();
        }

        // El aforo y la lectura del gym siguen respondiendo: es una opción de interfaz.
        using var read = Owner(gymId);
        var view = await read.ServiceProvider.GetRequiredService<GSM.Application.Gyms.Public.IGymReader>()
            .GetFeatureFlagsAsync(gymId);
        Assert.False(view!.ShowFrontDesk);
        var occupancy = await read.ServiceProvider.GetRequiredService<GSM.Application.Attendance.GetOccupancyHandler>()
            .HandleAsync(default);
        Assert.Equal(0, occupancy.PeopleInside);
    }

    // ---- soporte ----

    private static bool Required(OnboardingResponse state, string key) => state.Steps.Single(s => s.Key == key).Required;

    private async Task<OnboardingResponse> GetAsync(Guid gymId)
    {
        using var scope = Owner(gymId);
        return await scope.ServiceProvider.GetRequiredService<GetOnboardingHandler>().HandleAsync(default);
    }

    private async Task<Guid> SeedGymAsync(bool remote, bool routines, bool reportEquipment)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Options Gym", $"op-{Guid.NewGuid():N}"[..20]);
        var flags = GymFeatureFlags.Default(gym.Id);
        flags.Update(routines, true, true, true, true, true, false, reportEquipment);
        db.AddRange(gym, flags);
        if (remote)
        {
            var settings = CheckinSettings.Default(gym.Id);
            settings.Update(CheckinMode.Rotating, remoteTraining: true);
            db.Add(settings);
        }

        await db.SaveChangesAsync();
        return gym.Id;
    }

    private IServiceScope Owner(Guid gymId)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), gymId, UserRole.Owner);
        return scope;
    }
}
