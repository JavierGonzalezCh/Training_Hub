using GSM.API.Auth;
using GSM.Application.Identity;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Abstractions;
using GSM.Application.SuperAdmin;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Domain.Payments;
using GSM.Domain.SuperAdmin;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;
using AttendanceMethodEnum = GSM.Domain.Attendance.AttendanceMethod;

namespace GSM.IntegrationTests;

/// <summary>S7: la foto mensual del SaaS (superadmin.monthly_metrics), su tarea y su endpoint.</summary>
public sealed class MonthlyMetricsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly LastDayOfSeptember = new(2026, 9, 30);
    private static readonly DateOnly FirstOfOctober = new(2026, 10, 1);

    [Fact]
    public async Task First_run_of_the_next_month_creates_the_closed_month_with_the_expected_values()
    {
        var w = await SeedAsync();

        using var scope = System(FirstOfOctober);
        var created = await scope.ServiceProvider.GetRequiredService<RunMonthlyMetricsHandler>().HandleAsync(default);
        Assert.Equal(1, created);

        var row = await Row(scope, September);

        // MRR: pagador (100.000) + convertido (80.000). El nuevo en prueba, el inhabilitado y la cortesía no suman.
        Assert.Equal(180_000m, row.Mrr);

        // Cobrado: el pago de las 23:00 (hora de Colombia) del 30-sep es de septiembre aunque en UTC ya sea octubre;
        // el de la 01:00 del 1-oct, no.
        Assert.Equal(250_000m, row.Collected);

        Assert.Equal(1, row.NewGyms);            // el que empezó el 10-sep
        Assert.Equal(1, row.TrialGymsAtClose);   // ese mismo, aún en prueba
        Assert.Equal(1, row.ConvertedGyms);      // la prueba terminó el 31-ago y pagó
        Assert.Equal(1, row.LostGyms);           // el que pasó de mora a inhabilitado en septiembre
        Assert.Equal(2, row.ActiveGymsAtStart);  // pagador (al día) + el que estaba en mora
        Assert.Equal(2, row.ActiveGymsAtClose);  // pagador + convertido
        Assert.Equal(0.5m, row.Churn);           // 1 perdido / 2 activos al inicio
        Assert.Equal(1, row.TrialsNotConverted); // la prueba que no pagó: fuera del churn (P13)

        Assert.Equal(3, row.ActiveMembers);      // 2 + 1 con membresía que cubre el 30-sep; la vencida el 29 no
        Assert.Equal(3, row.CheckIns);           // 3 en septiembre; el del 1-oct no

        Assert.Equal(nameof(EffectiveSubscriptionStatus.Disabled), row.GymStates[w.Disabled.ToString()]);
    }

    [Fact]
    public async Task A_second_run_in_the_same_month_does_not_duplicate_or_recalculate()
    {
        var w = await SeedAsync();

        using (var scope = System(FirstOfOctober))
        {
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<RunMonthlyMetricsHandler>().HandleAsync(default));
        }

        // Entre las dos ejecuciones aparece un pago más de septiembre: la foto guardada no cambia.
        using (var scope = System(FirstOfOctober))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
            db.Add(SubscriptionPayment.Create(w.Payer, 999_000m, PaymentMethod.Cash, September, LastDayOfSeptember,
                clock.BusinessDayRange(new DateOnly(2026, 9, 5)).Start.AddHours(12), Guid.CreateVersion7(), null, null));
            await db.SaveChangesAsync();
        }

        using (var scope = System(new DateOnly(2026, 10, 20)))
        {
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<RunMonthlyMetricsHandler>().HandleAsync(default));
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            Assert.Equal(1, await db.Set<MonthlyMetric>().CountAsync());
            Assert.Equal(250_000m, (await Row(scope, September)).Collected);
        }
    }

    [Fact]
    public async Task The_next_month_starts_from_the_previous_snapshot_so_a_manual_suspension_counts_as_lost()
    {
        var w = await SeedAsync();

        using (var scope = System(FirstOfOctober))
        {
            await scope.ServiceProvider.GetRequiredService<RunMonthlyMetricsHandler>().HandleAsync(default);
        }

        // En octubre se suspende al pagador: la suspensión manual no deja historial, pero la foto de septiembre sí.
        using (var scope = System(new DateOnly(2026, 11, 1)))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            (await db.Set<Gym>().IgnoreQueryFilters().SingleAsync(g => g.Id == w.Payer)).SetCommercialStatus(GymStatus.Suspended);
            await db.SaveChangesAsync();

            await scope.ServiceProvider.GetRequiredService<RunMonthlyMetricsHandler>().HandleAsync(default);
            var october = await Row(scope, new DateOnly(2026, 10, 1));

            Assert.Equal(1, october.LostGyms);
            Assert.Equal(2, october.ActiveGymsAtStart);   // pagador + convertido, según la foto de septiembre
            Assert.Equal(0.5m, october.Churn);
        }
    }

    [Fact]
    public async Task Churn_is_null_when_there_were_no_active_gyms_at_the_start()
    {
        await SeedAsync(onlyTrialGym: true);

        using var scope = System(FirstOfOctober);
        await scope.ServiceProvider.GetRequiredService<RunMonthlyMetricsHandler>().HandleAsync(default);

        var row = await Row(scope, September);
        Assert.Null(row.Churn);
        Assert.Equal(0, row.ActiveGymsAtStart);
        Assert.Equal(1, row.TrialGymsAtClose);
    }

    [Fact]
    public async Task The_list_returns_at_most_twelve_months_oldest_first()
    {
        using var scope = System(FirstOfOctober);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        await db.Database.ExecuteSqlRawAsync("truncate table superadmin.monthly_metrics");
        for (var i = 0; i < 14; i++)
        {
            db.Add(MonthlyMetric.Create(new DateOnly(2025, 1, 1).AddMonths(i), 1000m * i, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new()));
        }

        await db.SaveChangesAsync();

        var response = await scope.ServiceProvider.GetRequiredService<ListMonthlyMetricsHandler>().HandleAsync(default);
        Assert.Equal(12, response.Months.Count);
        Assert.Equal(new DateOnly(2025, 3, 1), response.Months[0].Month);
        Assert.Equal(new DateOnly(2026, 2, 1), response.Months[^1].Month);
    }

    [Fact]
    public async Task Endpoint_is_superadmin_only_staff_gets_403()
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, CurrentUser>();
        }));
        var jwt = host.Services.GetRequiredService<IJwtTokenService>();

        HttpClient ClientFor(AuthenticatedUser user)
        {
            var client = host.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.CreateAccessToken(user).Value);
            return client;
        }

        var staff = ClientFor(new AuthenticatedUser(Guid.CreateVersion7(), "s@test.local", "Staff", UserRole.Staff, Guid.CreateVersion7()));
        var owner = ClientFor(new AuthenticatedUser(Guid.CreateVersion7(), "o@test.local", "Owner", UserRole.Owner, Guid.CreateVersion7()));
        var admin = ClientFor(new AuthenticatedUser(Guid.CreateVersion7(), "a@test.local", "Admin", UserRole.SuperAdmin, null));

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/metrics/monthly")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/admin/metrics/monthly")).StatusCode);

        var ok = await admin.GetAsync("/api/admin/metrics/monthly");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.NotNull(await ok.Content.ReadFromJsonAsync<MonthlyMetricsResponse>());
    }

    private IServiceScope System(DateOnly today)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(today);
        return scope;
    }

    private static Task<MonthlyMetric> Row(IServiceScope scope, DateOnly month)
        => scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<MonthlyMetric>()
            .AsNoTracking().SingleAsync(m => m.Month == month);

    private async Task<World> SeedAsync(bool onlyTrialGym = false)
    {
        using var scope = System(FirstOfOctober);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, " +
            "memberships.memberships, memberships.membership_plans, attendance.attendances, " +
            "superadmin.gym_subscriptions, superadmin.subscription_payments, superadmin.monthly_metrics " +
            "restart identity cascade;");

        // Nuevo en septiembre y todavía en prueba.
        var fresh = MakeGym(db, "m-new", GymStatus.Trial, September.AddDays(9), 0m, startedOn: new DateOnly(2026, 9, 10),
            paidThrough: new DateOnly(2026, 11, 10), trialEndsOn: new DateOnly(2026, 10, 10));
        if (onlyTrialGym)
        {
            await db.SaveChangesAsync();
            return new World(fresh, fresh, fresh);
        }

        // Su prueba terminó el 31-ago y pagó: convertido.
        MakeGym(db, "m-converted", GymStatus.Trial, September, 80_000m, startedOn: new DateOnly(2026, 8, 1),
            paidThrough: new DateOnly(2026, 10, 20), trialEndsOn: new DateOnly(2026, 8, 31));
        // P13: prueba hasta el 31-ago que nunca pagó; inhabilitado en septiembre. No es churn: va aparte.
        MakeGym(db, "m-trial-lost", GymStatus.Trial, September, 90_000m, startedOn: new DateOnly(2026, 8, 1),
            paidThrough: new DateOnly(2026, 8, 31), trialEndsOn: new DateOnly(2026, 8, 31));
        var payer = MakeGym(db, "m-payer", GymStatus.Active, September, 100_000m, startedOn: new DateOnly(2026, 6, 1),
            paidThrough: new DateOnly(2026, 11, 5));
        // Venció el 20-ago: en mora el 31-ago, inhabilitado el 30-sep (20-ago + 15 días = 4-sep).
        var disabled = MakeGym(db, "m-disabled", GymStatus.Active, September, 300_000m, startedOn: new DateOnly(2026, 3, 1),
            paidThrough: new DateOnly(2026, 8, 20));

        var plan = MembershipPlan.Create(payer, "Mensual", null, 50_000m, 30);
        var otherPlan = MembershipPlan.Create(disabled, "Mensual", null, 50_000m, 30);
        db.AddRange(plan, otherPlan);

        var joined = new DateOnly(2026, 1, 1);
        var m1 = Member.Create(payer, "Ana", "Uno", $"doc-{Guid.NewGuid():N}"[..12], joined);
        var m2 = Member.Create(payer, "Beto", "Dos", $"doc-{Guid.NewGuid():N}"[..12], joined);
        var m3 = Member.Create(payer, "Carla", "Tres", $"doc-{Guid.NewGuid():N}"[..12], joined);
        var m4 = Member.Create(disabled, "Dani", "Cuatro", $"doc-{Guid.NewGuid():N}"[..12], joined);
        db.AddRange(m1, m2, m3, m4);
        db.Add(Membership.Create(payer, m1.Id, plan.Id, September, 30));                 // cubre hasta el 30-sep
        db.Add(Membership.Create(payer, m2.Id, plan.Id, September, 30));
        db.Add(Membership.Create(payer, m3.Id, plan.Id, new DateOnly(2026, 8, 31), 30)); // termina el 29-sep: no cuenta
        db.Add(Membership.Create(disabled, m4.Id, otherPlan.Id, September, 30));        // otro gym: sí cuenta

        foreach (var (member, gym, day) in new[] { (m1, payer, 2), (m2, payer, 3), (m4, disabled, 4) })
        {
            db.Add(AttendanceEntity.Create(gym, member.Id, AttendanceMethodEnum.Manual,
                new DateTimeOffset(2026, 9, day, 15, 0, 0, TimeSpan.Zero), new DateOnly(2026, 9, day)));
        }

        db.Add(AttendanceEntity.Create(payer, m1.Id, AttendanceMethodEnum.Manual,
            new DateTimeOffset(2026, 10, 1, 15, 0, 0, TimeSpan.Zero), FirstOfOctober));

        // 23:00 hora de Colombia del último día = 04:00 UTC del 1-oct: cuenta en septiembre.
        var registeredBy = Guid.CreateVersion7();
        db.Add(SubscriptionPayment.Create(payer, 250_000m, PaymentMethod.BankTransfer, September, LastDayOfSeptember,
            clock.BusinessDayRange(LastDayOfSeptember).Start.AddHours(23), registeredBy, null, null));
        // 01:00 del 1-oct: es de octubre.
        db.Add(SubscriptionPayment.Create(payer, 100_000m, PaymentMethod.BankTransfer, FirstOfOctober, FirstOfOctober.AddDays(29),
            clock.BusinessDayRange(FirstOfOctober).Start.AddHours(1), registeredBy, null, null));

        await db.SaveChangesAsync();
        return new World(fresh, payer, disabled);
    }

    private static Guid MakeGym(
        GsmDbContext db, string slug, GymStatus status, DateOnly _, decimal price,
        DateOnly startedOn, DateOnly paidThrough, DateOnly? trialEndsOn = null)
    {
        var gym = Gym.Create(slug, $"{slug}-{Guid.NewGuid().ToString("N")[..8]}");
        if (status != GymStatus.Trial)
        {
            gym.SetCommercialStatus(status);
        }

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(GymSubscription.Create(gym.Id, price, startedOn, paidThrough, trialEndsOn));
        return gym.Id;
    }

    private sealed record World(Guid Fresh, Guid Payer, Guid Disabled);
}
