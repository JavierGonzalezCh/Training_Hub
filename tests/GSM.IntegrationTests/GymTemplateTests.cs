using GSM.Application.Achievements;
using GSM.Application.Attendance;
using GSM.Application.Attendance.Abstractions;
using GSM.Application.Gyms;
using GSM.Application.Gyms.Contracts;
using GSM.Application.Gyms.Events;
using GSM.Application.Memberships;
using GSM.Application.Routines;
using GSM.Application.SuperAdmin;
using GSM.Domain.Achievements;
using GSM.Domain.Attendance;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Memberships;
using GSM.Domain.Routines;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>T2: la plantilla del tipo de negocio fija la configuración inicial de cada módulo (docs/GSM-tipos-de-negocio.md §4).</summary>
public sealed class GymTemplateTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Guid AnyGym = Guid.CreateVersion7();

    [Theory]
    [InlineData(GymType.Gym, true, true, true, true, true, false, true, true)]
    [InlineData(GymType.Box, true, true, true, true, true, false, true, true)]
    [InlineData(GymType.Studio, false, true, true, true, true, false, false, true)]
    [InlineData(GymType.PersonalTrainer, true, false, true, true, false, false, false, false)]
    [InlineData(GymType.Academy, false, true, true, true, true, false, false, true)]
    [InlineData(GymType.Other, true, true, true, true, true, false, true, true)]
    public void Feature_flags_follow_the_module_table(
        GymType type, bool routines, bool classes, bool progress, bool gamification, bool community,
        bool membersCreate, bool reportEquipment, bool ranking)
    {
        var flags = GymFeatureFlags.ForType(AnyGym, type);

        Assert.Equal(routines, flags.ShowRoutines);
        Assert.Equal(classes, flags.ShowClasses);
        Assert.Equal(progress, flags.ShowProgress);
        Assert.Equal(gamification, flags.ShowGamification);
        Assert.Equal(membersCreate, flags.MembersCanCreateRoutines);
        Assert.Equal(reportEquipment, flags.MembersCanReportEquipment);
        Assert.Equal(ranking, flags.ShowRanking);
        // El entrenador personal no tiene puerta: sin check-in con QR ni comunidad.
        Assert.Equal(type != GymType.PersonalTrainer, flags.CheckinQr);
        Assert.Equal(community, flags.ShowCommunity);
    }

    [Theory]
    [InlineData(GymType.Gym, CheckinMode.Rotating, 120)]
    [InlineData(GymType.Box, CheckinMode.Static, 60)]
    [InlineData(GymType.Studio, CheckinMode.Static, 60)]
    [InlineData(GymType.PersonalTrainer, CheckinMode.Rotating, 120)]
    [InlineData(GymType.Academy, CheckinMode.Static, 90)]
    [InlineData(GymType.Other, CheckinMode.Rotating, 120)]
    public void Checkin_template_matches_the_table(GymType type, CheckinMode mode, int stay)
    {
        var template = CheckinTemplates.For(type);

        Assert.Equal(mode, template.Mode);
        Assert.Equal(stay, template.AssumedStayMinutes);
    }

    [Theory]
    [InlineData(GymType.Gym, 3)]
    [InlineData(GymType.Box, 3)]
    [InlineData(GymType.Studio, 0)]
    [InlineData(GymType.PersonalTrainer, 5)]
    [InlineData(GymType.Academy, 3)]
    [InlineData(GymType.Other, 3)]
    public void Membership_template_matches_the_table(GymType type, int graceDays)
    {
        var template = MembershipTemplates.For(type);

        Assert.Equal(graceDays, template.GraceDays);
        Assert.Equal(7, template.ExpiringDays);
        Assert.Equal(0m, template.EnrollmentFee);
    }

    [Theory]
    [InlineData(GymType.Gym, 6)]
    [InlineData(GymType.PersonalTrainer, 4)]
    [InlineData(GymType.Studio, 6)]
    public void Routine_template_matches_the_table(GymType type, int weeks)
        => Assert.Equal(weeks, RoutineSettingsTemplates.For(type).ReviewWeeks);

    [Fact]
    public void Achievement_catalog_differs_by_type_as_in_the_table()
    {
        static HashSet<string> Names(GymType t) => AchievementTemplates.For(t).Select(s => s.Name).ToHashSet();

        // Gym: asistencia (6), rachas (5), disciplina (5), fuerza (6), horarios (5), ranking (4), antigüedad y referidos.
        var gym = Names(GymType.Gym);
        Assert.Equal(33, gym.Count);
        Assert.Contains("Primeros Pasos", gym);
        Assert.Contains("Club de la Fuerza", gym);
        Assert.Contains("Madrugador I", gym);
        Assert.Contains("Número Uno", gym);
        Assert.DoesNotContain("Curioso", gym);

        var box = Names(GymType.Box);
        Assert.Contains("Curioso", box);
        Assert.Contains("Polifacético", box);
        Assert.Contains("Hércules", box);

        var studio = Names(GymType.Studio);
        Assert.Contains("Master de Estudio", studio);
        Assert.DoesNotContain("Rompiendo Barreras I", studio);
        Assert.DoesNotContain("Enfocado", studio);

        // El entrenador personal no tiene puerta: sin visitas, horarios ni ranking.
        var trainer = Names(GymType.PersonalTrainer);
        Assert.DoesNotContain("Primeros Pasos", trainer);
        Assert.DoesNotContain("Madrugador I", trainer);
        Assert.DoesNotContain("En el Tablero", trainer);
        Assert.Contains("Perfeccionista", trainer);
        Assert.Contains("Tonelaje I", trainer);
        Assert.Contains("Fuego Inicial", trainer);

        var academy = Names(GymType.Academy);
        Assert.Contains("Compañero de Fila", academy);
        Assert.DoesNotContain("Club de la Fuerza", academy);

        Assert.Equal(gym, Names(GymType.Other));
    }

    [Fact]
    public void Every_catalog_achievement_with_a_medal_points_to_a_file_that_exists()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Frontend/GSM.UI.Shared/wwwroot/achievements"));
        var seeds = Enum.GetValues<GymType>().SelectMany(AchievementTemplates.For).Where(s => s.Icon is not null).DistinctBy(s => s.Name).ToList();

        Assert.NotEmpty(seeds);
        Assert.All(seeds, s => Assert.True(File.Exists(Path.Combine(root, s.Icon + ".webp")), $"Falta la medalla de {s.Name}"));
        Assert.All(seeds, s => Assert.StartsWith(AchievementTemplates.IconBase, s.IconUrl));
    }

    [Theory]
    [InlineData(GymType.Studio)]
    [InlineData(GymType.PersonalTrainer)]
    public async Task Creating_a_gym_applies_the_template_to_every_module(GymType type)
    {
        var gymId = await CreateAsync(type);

        using var scope = SuperAdmin();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var flags = await db.Set<GymFeatureFlags>().IgnoreQueryFilters().SingleAsync(f => f.GymId == gymId);
        Assert.Equal(GymFeatureFlags.ForType(gymId, type).ShowRoutines, flags.ShowRoutines);
        Assert.Equal(GymFeatureFlags.ForType(gymId, type).CheckinQr, flags.CheckinQr);

        var checkin = await db.Set<CheckinSettings>().IgnoreQueryFilters().SingleAsync(s => s.GymId == gymId);
        Assert.Equal(CheckinTemplates.For(type).Mode, checkin.Mode);
        Assert.Equal(CheckinTemplates.For(type).AssumedStayMinutes, checkin.AssumedStayMinutes);
        Assert.Equal(CheckinTemplates.For(type).RemoteTraining, checkin.RemoteTraining);

        var membership = await db.Set<MembershipSettings>().IgnoreQueryFilters().SingleAsync(s => s.GymId == gymId);
        Assert.Equal(MembershipTemplates.For(type).GraceDays, membership.GraceDays);

        var routine = await db.Set<RoutineSettings>().IgnoreQueryFilters().SingleAsync(s => s.GymId == gymId);
        Assert.Equal(RoutineSettingsTemplates.For(type).ReviewWeeks, routine.ReviewWeeks);

        var seeded = await db.Set<Achievement>().IgnoreQueryFilters()
            .Where(a => a.GymId == gymId).Select(a => a.Name).ToListAsync();
        Assert.Equal(
            AchievementTemplates.For(type).Select(s => s.Name).Order(),
            seeded.Order());
    }

    [Fact]
    public async Task Retrying_the_template_handlers_does_not_overwrite_what_the_owner_already_changed()
    {
        var gymId = await CreateAsync(GymType.Studio);

        using (var scope = SuperAdmin())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ICheckinSettingsRepository>();
            var row = await repo.GetAsync(gymId);
            row!.Update(CheckinMode.Rotating, assumedStayMinutes: 200);
            await scope.ServiceProvider.GetRequiredService<GSM.Application.Common.Abstractions.IUnitOfWork>()
                .SaveChangesAsync(default);
        }

        using (var scope = SuperAdmin())
        {
            var @event = new GymCreated(gymId, "Reintento", Type: GymType.Studio);
            var handlers = scope.ServiceProvider
                .GetServices<GSM.Application.Common.Events.IDomainEventHandler<GymCreated>>()
                .Where(h => h is GymCreatedAppliesCheckinTemplateHandler
                    or GymCreatedAppliesMembershipTemplateHandler
                    or GymCreatedAppliesRoutineTemplateHandler
                    or SeedDefaultAchievementsHandler)
                .ToList();
            Assert.Equal(4, handlers.Count);
            foreach (var handler in handlers)
            {
                await handler.HandleAsync(@event, default);
            }
        }

        using var check = SuperAdmin();
        var db = check.ServiceProvider.GetRequiredService<GsmDbContext>();
        var checkin = await db.Set<CheckinSettings>().IgnoreQueryFilters().SingleAsync(s => s.GymId == gymId);
        Assert.Equal(200, checkin.AssumedStayMinutes);
        Assert.Equal(CheckinMode.Rotating, checkin.Mode);
        Assert.Equal(1, await db.Set<MembershipSettings>().IgnoreQueryFilters().CountAsync(s => s.GymId == gymId));
        Assert.Equal(1, await db.Set<RoutineSettings>().IgnoreQueryFilters().CountAsync(s => s.GymId == gymId));

        // La siembra de logros también es idempotente: un reintento no duplica ni choca con el índice único.
        Assert.Equal(
            AchievementTemplates.For(GymType.Studio).Count,
            await db.Set<Achievement>().IgnoreQueryFilters().CountAsync(a => a.GymId == gymId));
    }

    [Fact]
    public async Task The_admin_detail_reports_the_four_template_marks()
    {
        var gymId = await CreateAsync(GymType.Box);

        using (var scope = SuperAdmin())
        {
            var detail = await scope.ServiceProvider.GetRequiredService<GetGymDetailHandler>().HandleAsync(gymId, default);
            Assert.NotNull(detail.Template);
            Assert.True(detail.Template!.Complete);
        }

        // Un gym anterior a las plantillas (sin ajustes ni logros) se ve incompleto.
        Guid legacy;
        using (var scope = SuperAdmin())
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var created = await scope.ServiceProvider.GetRequiredService<CreateGymHandler>().HandleAsync(
                NewRequest(GymType.Gym), default);
            legacy = created.GymId;
            await db.Set<CheckinSettings>().IgnoreQueryFilters().Where(s => s.GymId == legacy).ExecuteDeleteAsync();
            await db.Set<RoutineSettings>().IgnoreQueryFilters().Where(s => s.GymId == legacy).ExecuteDeleteAsync();
        }

        using (var scope = SuperAdmin())
        {
            var detail = await scope.ServiceProvider.GetRequiredService<GetGymDetailHandler>().HandleAsync(legacy, default);
            Assert.False(detail.Template!.Checkin);
            Assert.True(detail.Template.Memberships);
            Assert.False(detail.Template.Routines);
            Assert.True(detail.Template.Achievements);
            Assert.False(detail.Template.Complete);
        }
    }

    private static CreateGymRequest NewRequest(GymType type)
        => new(
            "Gym plantilla", $"tpl-{Guid.NewGuid():N}"[..18], null, null, null, null,
            new CreateGymOwner($"owner-{Guid.NewGuid():N}@tpl.local", "Owner Plantilla"),
            TrialDays: 30,
            Type: type);

    private async Task<Guid> CreateAsync(GymType type)
    {
        using var scope = SuperAdmin();
        var created = await scope.ServiceProvider.GetRequiredService<CreateGymHandler>()
            .HandleAsync(NewRequest(type), default);
        return created.GymId;
    }

    private IServiceScope SuperAdmin()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>()
            .ActAs(Guid.CreateVersion7(), gymId: null, UserRole.SuperAdmin);
        return scope;
    }
}
