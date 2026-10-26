using GSM.Application.Gyms;
using GSM.Application.Gyms.Contracts;
using GSM.Application.SuperAdmin;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>T1: el gym nace con un tipo de negocio que se ve en la cartera y la ficha, y se puede filtrar.</summary>
public sealed class GymTypeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_gym_is_created_with_its_type_and_defaults_to_gym()
    {
        var studio = await CreateAsync(GymType.Studio);
        var plain = await CreateAsync(null);

        using var scope = SuperAdmin();
        var detail = scope.ServiceProvider.GetRequiredService<GetGymDetailHandler>();
        Assert.Equal("Studio", (await detail.HandleAsync(studio, default)).Type);
        Assert.Equal("Gym", (await detail.HandleAsync(plain, default)).Type);
    }

    [Fact]
    public async Task Portfolio_filters_by_type_and_rejects_an_unknown_one()
    {
        var box = await CreateAsync(GymType.Box);
        var trainer = await CreateAsync(GymType.PersonalTrainer);

        using var scope = SuperAdmin();
        var portfolio = scope.ServiceProvider.GetRequiredService<ListGymPortfolioHandler>();

        var boxes = await portfolio.HandleAsync(new PortfolioQuery { Type = "box" }, default);
        Assert.Contains(boxes.Items, i => i.GymId == box && i.Type == "Box");
        Assert.DoesNotContain(boxes.Items, i => i.GymId == trainer);
        Assert.All(boxes.Items, i => Assert.Equal("Box", i.Type));

        await Assert.ThrowsAsync<ValidationDomainException>(
            () => portfolio.HandleAsync(new PortfolioQuery { Type = "dojo" }, default));
    }

    [Fact]
    public void ChangeType_rejects_a_value_outside_the_catalog_and_keeps_the_rest()
    {
        var gym = Gym.Create("Mi box", "mi-box", type: GymType.Box);
        Assert.Equal(GymType.Box, gym.Type);

        gym.ChangeType(GymType.Academy);
        Assert.Equal(GymType.Academy, gym.Type);

        Assert.Throws<ValidationDomainException>(() => gym.ChangeType((GymType)99));
    }

    [Fact]
    public async Task The_super_admin_changes_the_type_without_touching_the_applied_template()
    {
        var gymId = await CreateAsync(GymType.Studio);

        using (var scope = SuperAdmin())
        {
            var changed = await scope.ServiceProvider.GetRequiredService<ChangeGymTypeHandler>()
                .HandleAsync(gymId, new ChangeGymTypeRequest(GymType.PersonalTrainer), default);
            Assert.Equal("PersonalTrainer", changed.Type);
        }

        using (var scope = SuperAdmin())
        {
            var db = scope.ServiceProvider.GetRequiredService<GSM.Infrastructure.Persistence.GsmDbContext>();
            // La plantilla del estudio sigue como se aplicó: cambiar el tipo no reaplica nada.
            var grace = await db.Set<GSM.Domain.Memberships.MembershipSettings>().IgnoreQueryFilters()
                .Where(s => s.GymId == gymId).Select(s => s.GraceDays).SingleAsync();
            Assert.Equal(0, grace);
            var flags = await db.Set<GymFeatureFlags>().IgnoreQueryFilters().SingleAsync(f => f.GymId == gymId);
            Assert.False(flags.ShowRoutines);
        }

        using var missing = SuperAdmin();
        await Assert.ThrowsAsync<NotFoundException>(() => missing.ServiceProvider
            .GetRequiredService<ChangeGymTypeHandler>()
            .HandleAsync(Guid.CreateVersion7(), new ChangeGymTypeRequest(GymType.Box), default));
    }

    [Fact]
    public async Task The_owner_changes_the_type_from_the_gym_data_and_leaving_it_out_keeps_it()
    {
        var gymId = await CreateAsync(GymType.Box);

        UpdateGymRequest Request(GymType? type) => new(
            "Mi box", null, null, null, null, "#B50016", "#0AAFCA",
            Address: "Calle 1 # 2-3", City: "Bogotá", Type: type);

        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>()
                .ActAs(Guid.CreateVersion7(), gymId, UserRole.Owner);
            var handler = scope.ServiceProvider.GetRequiredService<UpdateGymHandler>();

            Assert.Equal("Academy", (await handler.HandleAsync(Request(GymType.Academy), default)).Type);
            // Sin tipo en la petición (clientes anteriores) se conserva.
            Assert.Equal("Academy", (await handler.HandleAsync(Request(null), default)).Type);
        }
    }

    [Fact]
    public async Task The_admin_dashboard_splits_the_portfolio_by_business_type()
    {
        async Task<GSM.Application.SuperAdmin.Contracts.TypeMetricsRow?> RowAsync(string type)
        {
            using var scope = SuperAdmin();
            var dashboard = await scope.ServiceProvider.GetRequiredService<GSM.Application.SuperAdmin.GetAdminDashboardHandler>()
                .HandleAsync(default);
            return dashboard.ByType!.SingleOrDefault(t => t.Type == type);
        }

        var before = await RowAsync("Academy");
        await CreateAsync(GymType.Academy);
        await CreateAsync(GymType.Academy);
        var after = await RowAsync("Academy");

        Assert.NotNull(after);
        // Dos gyms nuevos, ambos en prueba (el alta de estas pruebas siempre lleva prueba de 30 días).
        Assert.Equal((before?.Gyms ?? 0) + 2, after!.Gyms);
        Assert.Equal((before?.Trial ?? 0) + 2, after.Trial);
        Assert.Equal(before?.Paying ?? 0, after.Paying);
        Assert.Equal(before?.Mrr ?? 0m, after.Mrr);
        Assert.InRange(after.ConversionPercent ?? 0, 0, 100);
    }

    [Fact]
    public async Task The_recommended_configuration_is_the_template_of_the_gym_type_and_writes_nothing()
    {
        var gymId = await CreateAsync(GymType.PersonalTrainer);

        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), gymId, UserRole.Owner);
        var result = await new GSM.API.Controllers.GymController().GetRecommendedConfiguration(
            scope.ServiceProvider.GetRequiredService<GetGymHandler>(), default);
        var recommended = Assert.IsType<GSM.Application.Gyms.Contracts.RecommendedConfiguration>(
            Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result).Value);

        Assert.Equal("PersonalTrainer", recommended.Type);
        Assert.False(recommended.Modules.ShowClasses);
        Assert.False(recommended.Modules.CheckinQr);
        Assert.False(recommended.Modules.ShowFrontDesk);
        Assert.True(recommended.Checkin.RemoteTraining);
        Assert.Equal("FirstLog", recommended.Checkin.TrainingEntryTrigger);
        Assert.Equal(5, recommended.Memberships.GraceDays);
        Assert.Equal(4, recommended.Routines.ReviewWeeks);
    }

    private async Task<Guid> CreateAsync(GymType? type)
    {
        using var scope = SuperAdmin();
        var request = new CreateGymRequest(
            "Gym de tipo", $"tipo-{Guid.NewGuid():N}"[..18], null, null, null, null,
            new CreateGymOwner($"owner-{Guid.NewGuid():N}@tipo.local", "Owner Tipo"),
            TrialDays: 30,
            Type: type ?? GymType.Gym);
        var created = await scope.ServiceProvider.GetRequiredService<CreateGymHandler>().HandleAsync(request, default);
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
