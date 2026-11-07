using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GSM.API.Auth;
using GSM.Application.Attendance;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Attendance.Events;
using GSM.Application.Attendance.Public;
using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Identity;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// A2: anular entradas (borrado lógico con motivo, últimos 7 días, permiso <c>ManageAttendance</c>,
/// evento <c>CheckInVoided</c>). Base de carril: <c>gsm_test_puerta</c>.
/// </summary>
public sealed class VoidCheckInTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";
    private static readonly DateOnly Today = new(2026, 10, 12);

    private static DateTimeOffset NoonOf(DateOnly day)
        => new GSM.Infrastructure.Common.SystemClock(
                Microsoft.Extensions.Options.Options.Create(new GSM.Infrastructure.Common.ClockOptions { TimeZone = "America/Bogota" }))
            .BusinessDayRange(day).Start.AddHours(12);

    public sealed class VoidRecorder : IDomainEventHandler<CheckInVoided>
    {
        public ConcurrentQueue<CheckInVoided> Events { get; } = new();

        public Task HandleAsync(CheckInVoided domainEvent, CancellationToken cancellationToken = default)
        {
            Events.Enqueue(domainEvent);
            return Task.CompletedTask;
        }
    }

    // ---- caso de uso ----

    [Fact]
    public async Task Voiding_keeps_the_row_as_a_trace_with_who_when_and_why()
    {
        var w = await SeedDomainAsync();
        var id = await EnterAsync(w, NoonOf(Today));

        await VoidAsync(w, id, "  Registré al miembro equivocado  ", NoonOf(Today).AddMinutes(5));

        using var s = Scope(w, NoonOf(Today).AddMinutes(10));
        var row = await s.ServiceProvider.GetRequiredService<GsmDbContext>()
            .Set<GSM.Domain.Attendance.Attendance>().SingleAsync(a => a.Id == id);
        Assert.True(row.IsVoided);
        Assert.Equal(NoonOf(Today).AddMinutes(5), row.VoidedAt);
        Assert.Equal(w.StaffUserId, row.VoidedBy);
        Assert.Equal("Registré al miembro equivocado", row.VoidReason);
    }

    [Fact]
    public async Task A_reason_is_mandatory_and_voiding_twice_is_a_conflict()
    {
        var w = await SeedDomainAsync();
        var id = await EnterAsync(w, NoonOf(Today));

        await Assert.ThrowsAsync<ValidationDomainException>(() => VoidAsync(w, id, "  ", NoonOf(Today)));
        await Assert.ThrowsAsync<ValidationDomainException>(() => VoidAsync(w, id, "ab", NoonOf(Today)));

        await VoidAsync(w, id, "Error de recepción", NoonOf(Today));
        await Assert.ThrowsAsync<ConflictException>(() => VoidAsync(w, id, "Otra vez", NoonOf(Today)));
    }

    [Fact]
    public async Task A_voided_entry_stops_counting_for_visits_and_streak_and_frees_the_day()
    {
        var w = await SeedDomainAsync();
        var id = await EnterAsync(w, NoonOf(Today));

        using (var before = Scope(w, NoonOf(Today).AddHours(1)))
        {
            var reader = before.ServiceProvider.GetRequiredService<IAttendanceReader>();
            Assert.Equal(1, await reader.GetVisitCountAsync(w.MemberId));
            Assert.Equal(1, await reader.GetStreakAsync(w.MemberId));
        }

        // Sin anular, el miembro no puede volver a entrar hoy.
        await Assert.ThrowsAsync<ConflictException>(() => EnterAsync(w, NoonOf(Today).AddHours(1)));

        await VoidAsync(w, id, "Registré al miembro equivocado", NoonOf(Today).AddHours(1));

        using (var after = Scope(w, NoonOf(Today).AddHours(1)))
        {
            var reader = after.ServiceProvider.GetRequiredService<IAttendanceReader>();
            Assert.Equal(0, await reader.GetVisitCountAsync(w.MemberId));
            Assert.Equal(0, await reader.GetStreakAsync(w.MemberId));
            Assert.Empty(await reader.GetAttendanceDatesAsync(w.MemberId));
            Assert.Null(await reader.GetLastVisitAsync(w.MemberId));
            Assert.Equal(0, await reader.GetTodayCountAsync(w.GymId));

            var card = await after.ServiceProvider.GetRequiredService<GetReceptionCardHandler>()
                .HandleAsync(w.MemberId, default);
            Assert.True(card.CanRegister);
            Assert.Equal(0, card.EntriesToday);
        }

        // El día quedó libre: puede registrar la entrada de nuevo.
        var again = await EnterAsync(w, NoonOf(Today).AddHours(1));
        Assert.NotEqual(id, again);
    }

    [Fact]
    public async Task Voiding_the_only_entry_of_a_day_with_two_entries_keeps_the_day_counted()
    {
        var w = await SeedDomainAsync();
        await SetRuleAsync(w);
        var first = await EnterAsync(w, NoonOf(Today));
        await EnterAsync(w, NoonOf(Today).AddHours(3));

        await VoidAsync(w, first, "Duplicada", NoonOf(Today).AddHours(4));

        using var s = Scope(w, NoonOf(Today).AddHours(4));
        Assert.Equal(1, await s.ServiceProvider.GetRequiredService<IAttendanceReader>().GetVisitCountAsync(w.MemberId));
    }

    [Fact]
    public async Task Only_entries_of_the_last_seven_days_can_be_voided()
    {
        var w = await SeedDomainAsync();
        var seven = Today.AddDays(-7);
        var eight = Today.AddDays(-8);
        var okId = await SeedEntryAsync(w, seven);
        var oldId = await SeedEntryAsync(w, eight);

        await Assert.ThrowsAsync<ConflictException>(() => VoidAsync(w, oldId, "Muy vieja", NoonOf(Today)));
        await VoidAsync(w, okId, "Dentro de la ventana", NoonOf(Today));
    }

    [Fact]
    public async Task An_entry_of_another_gym_cannot_be_voided()
    {
        var w = await SeedDomainAsync();
        var other = await SeedDomainAsync();
        var id = await EnterAsync(w, NoonOf(Today));

        await Assert.ThrowsAsync<NotFoundException>(() => VoidAsync(other, id, "No es mía", NoonOf(Today)));
    }

    [Fact]
    public async Task The_list_hides_voided_entries_unless_asked_and_then_shows_who_and_why()
    {
        var w = await SeedDomainAsync();
        var id = await EnterAsync(w, NoonOf(Today));
        await VoidAsync(w, id, "Error de recepción", NoonOf(Today));

        using var s = Scope(w, NoonOf(Today).AddHours(1));
        var handler = s.ServiceProvider.GetRequiredService<ListAttendanceHandler>();

        var hidden = await handler.HandleAsync(new ListAttendanceQuery { MemberId = w.MemberId }, default);
        Assert.Empty(hidden.Items);

        var shown = await handler.HandleAsync(new ListAttendanceQuery { MemberId = w.MemberId, IncludeVoided = true }, default);
        var item = Assert.Single(shown.Items);
        Assert.True(item.Voided);
        Assert.Equal("Error de recepción", item.VoidReason);
        Assert.Equal(w.StaffUserId, item.VoidedBy);
        Assert.NotNull(item.VoidedAt);
    }

    [Fact]
    public async Task Voiding_publishes_CheckInVoided_with_what_the_tiquetera_needs_to_give_the_entry_back()
    {
        var w = await SeedDomainAsync();
        var id = await EnterAsync(w, NoonOf(Today));

        var recorder = new VoidRecorder();
        await using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.AddSingleton(recorder);
            services.AddSingleton<IDomainEventHandler<CheckInVoided>>(recorder);
        }));
        using (var scope = host.Services.CreateScope())
        {
            var user = scope.ServiceProvider.GetRequiredService<MutableCurrentUser>();
            user.ActAs(w.StaffUserId, w.GymId, UserRole.Staff);
            scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(NoonOf(Today).AddMinutes(1), Today);
            await scope.ServiceProvider.GetRequiredService<VoidCheckInHandler>()
                .HandleAsync(id, new VoidCheckInRequest("Error de recepción"), default);
        }

        var evt = Assert.Single(recorder.Events);
        Assert.Equal(id, evt.AttendanceId);
        Assert.Equal(w.GymId, evt.GymId);
        Assert.Equal(w.MemberId, evt.MemberId);
        Assert.Equal(Today, evt.AttendanceDate);
        Assert.Equal(w.StaffUserId, evt.VoidedBy);
        Assert.Equal("Error de recepción", evt.Reason);
    }

    // ---- HTTP con identidad real: permiso ManageAttendance ----

    [Fact]
    public async Task Staff_without_ManageAttendance_gets_403_and_with_it_204()
    {
        var w = await SeedHttpAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var staff = await LoggedInAsync(host, w.StaffEmail);

        var id1 = await CheckInAsync(staff, w.MemberId);

        var denied = await staff.PostAsJsonAsync($"/api/attendance/{id1}/void", new VoidCheckInRequest("Error de recepción"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var grant = await owner.PutAsJsonAsync(
            $"/api/gym/staff/{w.StaffId}/permissions", new UpdateStaffPermissionsRequest("reception", ["manage_attendance"]));
        Assert.True(grant.IsSuccessStatusCode, await grant.Content.ReadAsStringAsync());
        var staffWith = await LoggedInAsync(host, w.StaffEmail);

        var ok = await staffWith.PostAsJsonAsync($"/api/attendance/{id1}/void", new VoidCheckInRequest("Error de recepción"));
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        // El motivo sigue siendo obligatorio.
        var id2 = await CheckInAsync(staffWith, w.MemberId);
        var noReason = await staffWith.PostAsJsonAsync($"/api/attendance/{id2}/void", new VoidCheckInRequest("  "));
        Assert.True(noReason.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_owner_can_void_without_any_explicit_permission()
    {
        var w = await SeedHttpAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);

        var id = await CheckInAsync(owner, w.MemberId);
        var ok = await owner.PostAsJsonAsync($"/api/attendance/{id}/void", new VoidCheckInRequest("Error de recepción"));
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        var missing = await owner.PostAsJsonAsync($"/api/attendance/{Guid.NewGuid()}/void", new VoidCheckInRequest("No existe"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Voiding_requires_a_session()
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync($"/api/attendance/{Guid.NewGuid()}/void", new VoidCheckInRequest("Sin sesión"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- ayudantes de dominio ----

    private IServiceScope Scope(DomainWorld w, DateTimeOffset now)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.StaffUserId, w.GymId, UserRole.Staff);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(now, DateOnly.FromDateTime(now.UtcDateTime.AddHours(-5)));
        return scope;
    }

    private async Task<Guid> EnterAsync(DomainWorld w, DateTimeOffset now)
    {
        using var s = Scope(w, now);
        var (response, _) = await s.ServiceProvider.GetRequiredService<CheckInHandler>()
            .HandleAsync(new CheckInRequest(w.MemberId, null), default);
        return response.AttendanceId;
    }

    private async Task VoidAsync(DomainWorld w, Guid id, string reason, DateTimeOffset now)
    {
        using var s = Scope(w, now);
        await s.ServiceProvider.GetRequiredService<VoidCheckInHandler>()
            .HandleAsync(id, new VoidCheckInRequest(reason), default);
    }

    private async Task SetRuleAsync(DomainWorld w)
    {
        using var s = Scope(w, NoonOf(Today));
        await s.ServiceProvider.GetRequiredService<SetEntryRuleHandler>()
            .HandleAsync(w.MemberId, new SetEntryRuleRequest(2, "Doble sesión", null), default);
    }

    /// <summary>Inserta una entrada directa en una fecha pasada (el reloj del caso de uso no retrocede).</summary>
    private async Task<Guid> SeedEntryAsync(DomainWorld w, DateOnly day)
    {
        using var s = Scope(w, NoonOf(Today));
        var db = s.ServiceProvider.GetRequiredService<GsmDbContext>();
        var entry = GSM.Domain.Attendance.Attendance.Create(
            w.GymId, w.MemberId, GSM.Domain.Attendance.AttendanceMethod.Manual, NoonOf(day), day);
        db.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task<DomainWorld> SeedDomainAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Void Gym", $"vc-{Guid.NewGuid():N}"[..16]);
        var member = Member.Create(gym.Id, "Ana", "Prueba", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-100));
        var plan = MembershipPlan.Create(gym.Id, "Mensual", null, 50000m, 30);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), member, plan,
            Membership.Create(gym.Id, member.Id, plan.Id, Today.AddDays(-10), 30));
        await db.SaveChangesAsync();
        return new DomainWorld(gym.Id, member.Id, Guid.CreateVersion7());
    }

    private sealed record DomainWorld(Guid GymId, Guid MemberId, Guid StaffUserId);

    // ---- ayudantes HTTP ----

    private WebApplicationFactory<Program> RealIdentityHost()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, CurrentUser>();
        }));

    private static async Task<HttpClient> LoggedInAsync(WebApplicationFactory<Program> host, string email)
    {
        var login = await host.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();
        var tokens = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Tokens!;
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    private static async Task<Guid> CheckInAsync(HttpClient client, Guid memberId)
    {
        var response = await client.PostAsJsonAsync("/api/attendance/checkin", new CheckInRequest(memberId, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CheckInResponse>())!.AttendanceId;
    }

    private async Task<HttpWorld> SeedHttpAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("Void Http Gym", $"voidh-{suffix}");
        // El host HTTP corre con el reloj real: la membresía se siembra con la fecha real, no con la que otro test de esta
        // clase haya dejado fijada (si no, desde que pasa esa fecha la membresía sale vencida y la entrada da 422).
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
        clock.Unpin();
        var today = clock.Today;
        var member = Member.Create(gym.Id, "Ana", "Prueba", $"doc-{suffix}", today.AddDays(-100));
        var plan = MembershipPlan.Create(gym.Id, "Mensual", null, 50000m, 30);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), member, plan,
            Membership.Create(gym.Id, member.Id, plan.Id, today.AddDays(-10), 30));
        await db.SaveChangesAsync();

        var ownerEmail = $"owner-{suffix}@test.local";
        var staffEmail = $"staff-{suffix}@test.local";
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Void Owner", Password, UserRole.Owner, gym.Id);
        var staffId = await provisioning.CreateGymUserAsync(staffEmail, "Void Staff", Password, UserRole.Staff, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();
        return new HttpWorld(member.Id, ownerEmail, staffId, staffEmail);
    }

    private sealed record HttpWorld(Guid MemberId, string OwnerEmail, Guid StaffId, string StaffEmail);
}
