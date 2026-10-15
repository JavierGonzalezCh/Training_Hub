using GSM.Application.Memberships;
using GSM.Application.Memberships.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Members;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>MS3: planes en meses del calendario de punta a punta (planes, asignar, renovar, vista previa).</summary>
public sealed class MembershipMonthsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Month_plan_is_stored_and_listed_with_its_unit()
    {
        var (gymId, staffId, _) = await SeedAsync();
        using var scope = Staff(gymId, staffId);
        var sp = scope.ServiceProvider;

        var created = await sp.GetRequiredService<CreatePlanHandler>()
            .HandleAsync(new CreatePlanRequest("Trimestral", null, 200000m, 3, "months"), default);
        var legacy = await sp.GetRequiredService<CreatePlanHandler>()
            .HandleAsync(new CreatePlanRequest("Quincenal", null, 40000m, 15), default); // sin unidad = días

        Assert.Equal("months", created.DurationUnit);
        Assert.Equal(3, created.DurationValue);
        Assert.Equal("days", legacy.DurationUnit);
        Assert.Equal(15, legacy.DurationValue);

        var updated = await sp.GetRequiredService<UpdatePlanHandler>()
            .HandleAsync(legacy.Id, new UpdatePlanRequest("Quincenal", null, 40000m, 1, "months"), default);
        Assert.Equal("months", updated.DurationUnit);
        Assert.Equal(1, updated.DurationValue);
    }

    [Fact]
    public async Task Unknown_unit_or_out_of_range_months_are_rejected()
    {
        var (gymId, staffId, _) = await SeedAsync();
        using var scope = Staff(gymId, staffId);
        var handler = scope.ServiceProvider.GetRequiredService<CreatePlanHandler>();

        await Assert.ThrowsAsync<ValidationDomainException>(() => handler.HandleAsync(
            new CreatePlanRequest("Rara", null, 1m, 1, "weeks"), default));
        await Assert.ThrowsAsync<ValidationDomainException>(() => handler.HandleAsync(
            new CreatePlanRequest("Enorme", null, 1m, 121, "months"), default));
    }

    [Fact]
    public async Task Assign_month_plan_on_january_31st_ends_on_february_27th_and_preview_agrees()
    {
        var (gymId, staffId, memberId) = await SeedAsync();
        using var scope = Staff(gymId, staffId);
        var sp = scope.ServiceProvider;
        var planId = (await sp.GetRequiredService<CreatePlanHandler>()
            .HandleAsync(new CreatePlanRequest("Mensual cal", null, 80000m, 1, "months"), default)).Id;

        var start = new DateOnly(2041, 1, 31); // 2041 no es bisiesto; membresía nueva = fecha libre
        var preview = await sp.GetRequiredService<PreviewMembershipHandler>()
            .HandleAsync(memberId, planId, start, null, default);
        var assigned = await sp.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(memberId, new AssignMembershipRequest(planId, start), default);

        Assert.Equal(new DateOnly(2041, 2, 27), preview.EndDate);
        Assert.Equal(preview.EndDate, assigned.EndDate);
        Assert.Equal(start, assigned.StartDate);
        Assert.Equal(31, assigned.CycleDay);
    }

    [Fact]
    public async Task Leap_year_february_ends_on_the_28th()
    {
        var (gymId, staffId, memberId) = await SeedAsync();
        using var scope = Staff(gymId, staffId);
        var sp = scope.ServiceProvider;
        var planId = (await sp.GetRequiredService<CreatePlanHandler>()
            .HandleAsync(new CreatePlanRequest("Mensual bis", null, 80000m, 1, "months"), default)).Id;

        var assigned = await sp.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(memberId, new AssignMembershipRequest(planId, new DateOnly(2040, 1, 31)), default);

        Assert.Equal(new DateOnly(2040, 2, 28), assigned.EndDate); // 2040 es bisiesto
    }

    [Fact]
    public async Task Chained_renewal_keeps_the_cycle_day_and_preview_matches_the_created_end()
    {
        var (gymId, staffId, memberId) = await SeedAsync();
        using var scope = Staff(gymId, staffId);
        var sp = scope.ServiceProvider;
        var planId = (await sp.GetRequiredService<CreatePlanHandler>()
            .HandleAsync(new CreatePlanRequest("Mensual enc", null, 80000m, 1, "months"), default)).Id;

        var first = await sp.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(memberId, new AssignMembershipRequest(planId, new DateOnly(2041, 1, 31)), default);

        // La primera está lejos en el futuro: la siguiente es "early" y arranca al día siguiente.
        var preview = await sp.GetRequiredService<PreviewMembershipHandler>()
            .HandleAsync(memberId, planId, null, null, default);
        var second = await sp.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(memberId, new AssignMembershipRequest(planId, null), default);
        var third = await sp.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(memberId, new AssignMembershipRequest(planId, null), default);

        Assert.Equal(new DateOnly(2041, 2, 27), first.EndDate);
        Assert.Equal(new DateOnly(2041, 2, 28), second.StartDate);
        Assert.Equal(new DateOnly(2041, 3, 30), second.EndDate); // conserva el ciclo del 31
        Assert.Equal(preview.EndDate, second.EndDate);
        Assert.Equal(31, second.CycleDay);
        Assert.Equal(new DateOnly(2041, 3, 31), third.StartDate);
        Assert.Equal(new DateOnly(2041, 4, 29), third.EndDate);
    }

    [Fact]
    public async Task Existing_day_plans_keep_their_behaviour()
    {
        var (gymId, staffId, memberId) = await SeedAsync();
        using var scope = Staff(gymId, staffId);
        var sp = scope.ServiceProvider;
        var planId = (await sp.GetRequiredService<CreatePlanHandler>()
            .HandleAsync(new CreatePlanRequest("Mensual dias", null, 80000m, 30), default)).Id;

        var assigned = await sp.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(memberId, new AssignMembershipRequest(planId, new DateOnly(2041, 1, 31)), default);

        Assert.Equal(new DateOnly(2041, 3, 1), assigned.EndDate); // 31-ene + 29 días
        Assert.Null(assigned.CycleDay);
    }

    private IServiceScope Staff(Guid gymId, Guid staffId)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>()
            .ActAs(staffId, gymId, GSM.Domain.Identity.UserRole.Staff);
        return scope;
    }

    private async Task<(Guid GymId, Guid StaffId, Guid MemberId)> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<GSM.Application.Common.Abstractions.IClock>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, members.member_invitations, memberships.membership_plans, memberships.memberships restart identity cascade;");

        var gym = Gym.Create("Months Gym", $"months-{Guid.NewGuid():N}"[..20]);
        var staff = new GSM.Infrastructure.Identity.AppUser
        {
            UserName = "staff@months.local",
            Email = "staff@months.local",
            NormalizedUserName = "STAFF@MONTHS.LOCAL",
            NormalizedEmail = "STAFF@MONTHS.LOCAL",
            EmailConfirmed = true,
            FullName = "Months Staff",
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        gym.AssignOwner(staff.Id);
        var member = Member.Create(gym.Id, "Months", "Member", "doc-months", clock.Today);

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);
        db.Add(member);
        await db.SaveChangesAsync();

        return (gym.Id, staff.Id, member.Id);
    }
}
