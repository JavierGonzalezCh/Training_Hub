using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GSM.API.Auth;
using GSM.Application.Attendance;
using GSM.Application.Attendance.Abstractions;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Classes;
using GSM.Application.Classes.Contracts;
using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms;
using GSM.Application.Gyms.Contracts;
using GSM.Application.Gyms.Public;
using GSM.Application.Identity;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Public;
using GSM.Domain.Attendance;
using GSM.Domain.Classes;
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
using AttendanceEntity = GSM.Domain.Attendance.Attendance;

namespace GSM.IntegrationTests;

/// <summary>G3: festivos de Colombia, horario de atención, excepciones y el rechazo <c>gym_closed</c>.</summary>
public sealed class OpeningHoursTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Monday = new(2026, 10, 5);      // lunes común
    private static readonly DateOnly HolidayMonday = new(2026, 10, 12); // Día de la Raza

    // ---------- Festivos de Colombia (puros, sin base de datos) ----------

    [Fact]
    public void Colombian_holidays_of_2026_match_the_official_calendar()
    {
        var expected = new[]
        {
            (1, 1), (1, 12), (3, 23), (4, 2), (4, 3), (5, 1), (5, 18), (6, 8), (6, 15), (6, 29),
            (7, 20), (8, 7), (8, 17), (10, 12), (11, 2), (11, 16), (12, 8), (12, 25),
        }.Select(t => new DateOnly(2026, t.Item1, t.Item2));

        Assert.Equal(expected, ColombianHolidays.ForYear(2026).Select(h => h.Date));
    }

    [Fact]
    public void Colombian_holidays_of_2027_match_the_official_calendar()
    {
        var expected = new[]
        {
            (1, 1), (1, 11), (3, 22), (3, 25), (3, 26), (5, 1), (5, 10), (5, 31), (6, 7), (7, 5),
            (7, 20), (8, 7), (8, 16), (10, 18), (11, 1), (11, 15), (12, 8), (12, 25),
        }.Select(t => new DateOnly(2027, t.Item1, t.Item2));

        Assert.Equal(expected, ColombianHolidays.ForYear(2027).Select(h => h.Date));
    }

    [Fact]
    public void Moved_holidays_always_land_on_a_monday_and_easter_ones_are_named()
    {
        foreach (var year in new[] { 2026, 2027, 2028, 2030 })
        {
            var all = ColombianHolidays.ForYear(year);
            Assert.Equal(18, all.Count);
            foreach (var name in new[] { "Día de los Reyes Magos", "Día de San José", "Ascensión del Señor", "Corpus Christi", "Sagrado Corazón de Jesús" })
            {
                Assert.Equal(DayOfWeek.Monday, all.Single(h => h.Name == name).Date.DayOfWeek);
            }

            Assert.Equal(DayOfWeek.Thursday, all.Single(h => h.Name == "Jueves Santo").Date.DayOfWeek);
            Assert.Equal(DayOfWeek.Friday, all.Single(h => h.Name == "Viernes Santo").Date.DayOfWeek);
        }
    }

    // ---------- Cálculo del horario (puro) ----------

    [Fact]
    public void Schedule_resolves_split_slots_holidays_and_special_days_in_that_priority()
    {
        var schedule = new OpeningSchedule(Week(), new Dictionary<DateOnly, SpecialDayData>
        {
            [new DateOnly(2026, 10, 6)] = new(false, [new TimeSlot(new TimeOnly(10, 0), new TimeOnly(11, 0))]),
            [new DateOnly(2026, 10, 7)] = new(true, []),
        });

        // Lunes común, partido: 5-12 y 15-22.
        Assert.True(schedule.Evaluate(Monday, new TimeOnly(11, 59)).IsOpen);
        var gap = schedule.Evaluate(Monday, new TimeOnly(13, 0));
        Assert.False(gap.IsOpen);
        Assert.Equal(Monday, gap.NextOpensOn);
        Assert.Equal(new TimeOnly(15, 0), gap.NextOpensAt);
        Assert.Equal(new TimeOnly(22, 0), schedule.Evaluate(Monday, new TimeOnly(16, 0)).ClosesAt);

        // El festivo usa el horario de festivo (7-13), no el del lunes.
        Assert.False(schedule.Evaluate(HolidayMonday, new TimeOnly(6, 0)).IsOpen);
        Assert.True(schedule.Evaluate(HolidayMonday, new TimeOnly(8, 0)).IsOpen);
        Assert.False(schedule.Evaluate(HolidayMonday, new TimeOnly(15, 30)).IsOpen);

        // La excepción manda sobre la semana.
        Assert.False(schedule.Evaluate(new DateOnly(2026, 10, 6), new TimeOnly(8, 0)).IsOpen);
        Assert.True(schedule.Evaluate(new DateOnly(2026, 10, 6), new TimeOnly(10, 30)).IsOpen);

        // Día cerrado: abre al siguiente día con horario.
        var closed = schedule.Evaluate(new DateOnly(2026, 10, 7), new TimeOnly(8, 0));
        Assert.False(closed.IsOpen);
        Assert.Equal(new DateOnly(2026, 10, 8), closed.NextOpensOn);
        Assert.Equal(new TimeOnly(5, 0), closed.NextOpensAt);
    }

    [Fact]
    public void Messages_say_today_tomorrow_or_the_weekday()
    {
        var schedule = new OpeningSchedule(Week(), new Dictionary<DateOnly, SpecialDayData>());
        var night = schedule.Evaluate(Monday, new TimeOnly(23, 0));

        Assert.Equal("Cerrado · abre mañana a las 5:00", OpenStatusText.Badge(night, Monday));
        Assert.Equal("El gimnasio está cerrado. Abre mañana a las 5:00", OpenStatusText.ClosedMessage(night, Monday));
        Assert.Equal("Abierto · cierra a las 22:00", OpenStatusText.Badge(schedule.Evaluate(Monday, new TimeOnly(16, 0)), Monday));
        Assert.Equal("Cerrado · abre hoy a las 15:00", OpenStatusText.Badge(schedule.Evaluate(Monday, new TimeOnly(13, 0)), Monday));
    }

    [Theory]
    [InlineData("09:00", "09:00")]
    [InlineData("10:00", "09:00")]
    public void A_slot_must_open_before_it_closes(string opens, string closes)
    {
        Assert.Throws<ValidationDomainException>(() => OpeningSlots.Normalize(
            [new TimeSlot(TimeOnly.Parse(opens), TimeOnly.Parse(closes))], "Lunes"));
    }

    [Fact]
    public void Overlapping_or_empty_slots_are_rejected()
    {
        Assert.Throws<ValidationDomainException>(() => OpeningSlots.Normalize([], "Lunes"));
        Assert.Throws<ValidationDomainException>(() => OpeningSlots.Normalize(
            [new TimeSlot(new TimeOnly(5, 0), new TimeOnly(12, 0)), new TimeSlot(new TimeOnly(11, 0), new TimeOnly(15, 0))], "Lunes"));
    }

    // ---------- IGymReader.IsOpenAt (con base de datos, reloj en hora de Colombia) ----------

    [Fact]
    public async Task IsOpenAt_uses_the_gym_local_time_not_utc_around_midnight()
    {
        var gymId = await SeedGymAsync();
        await SaveWeekAsync(gymId, mon: [("20:00", "23:59")], tue: [("05:00", "22:00")]);

        // 2026-10-06 04:30 UTC = lunes 23:30 en Colombia: el lunes sigue abierto hasta las 23:59.
        var lateMonday = new DateTimeOffset(2026, 10, 6, 4, 30, 0, TimeSpan.Zero);
        // 10:30 UTC = martes 05:30 local: ya abrió el martes.
        var tuesdayOpening = new DateTimeOffset(2026, 10, 6, 10, 30, 0, TimeSpan.Zero);
        // 2026-10-06 09:30 UTC = martes 04:30 local: todavía cerrado.
        var beforeOpening = new DateTimeOffset(2026, 10, 6, 9, 30, 0, TimeSpan.Zero);

        using var scope = factory.CreateScopeWithMigration();
        var reader = scope.ServiceProvider.GetRequiredService<IGymReader>();

        Assert.True(await reader.IsOpenAtAsync(gymId, lateMonday));
        Assert.True(await reader.IsOpenAtAsync(gymId, tuesdayOpening));
        var before = await reader.GetOpenStatusAsync(gymId, beforeOpening);
        Assert.False(before.IsOpen);
        Assert.Equal(new DateOnly(2026, 10, 6), before.NextOpensOn);
        Assert.Equal(new TimeOnly(5, 0), before.NextOpensAt);
    }

    [Fact]
    public async Task A_gym_without_a_schedule_is_always_open()
    {
        var gymId = await SeedGymAsync();

        using var scope = factory.CreateScopeWithMigration();
        var reader = scope.ServiceProvider.GetRequiredService<IGymReader>();

        Assert.True(await reader.IsOpenAtAsync(gymId, new DateTimeOffset(2026, 10, 6, 7, 0, 0, TimeSpan.Zero)));
        Assert.False((await reader.GetOpenStatusAsync(gymId, DateTimeOffset.UtcNow)).HasSchedule);
    }

    [Fact]
    public async Task Holiday_schedule_special_hours_and_a_closed_day_all_apply_to_the_reader()
    {
        var gymId = await SeedGymAsync();
        await SaveWeekAsync(gymId);

        // Festivo 12-oct: horario de festivo 7-13.
        Assert.False(await IsOpenAsync(gymId, HolidayMonday, 15, 0));
        Assert.True(await IsOpenAsync(gymId, HolidayMonday, 8, 0));

        // Ajusto el festivo con otro horario (14 a 18).
        await UpsertSpecialAsync(gymId, HolidayMonday, closed: false, [("14", "18")]);
        Assert.True(await IsOpenAsync(gymId, HolidayMonday, 15, 0));
        Assert.False(await IsOpenAsync(gymId, HolidayMonday, 8, 0));

        // Y cierro el martes completo.
        var tuesday = Monday.AddDays(8); // 2026-10-13
        await UpsertSpecialAsync(gymId, tuesday, closed: true, []);
        Assert.False(await IsOpenAsync(gymId, tuesday, 10, 0));
        var status = await StatusAsync(gymId, tuesday, 10, 0);
        Assert.Equal(tuesday.AddDays(1), status.NextOpensOn);

        // Quitar la excepción devuelve el horario normal.
        using (var scope = Owner(gymId, Monday))
        {
            await scope.ServiceProvider.GetRequiredService<DeleteSpecialDayHandler>().HandleAsync(tuesday, default);
        }

        Assert.True(await IsOpenAsync(gymId, tuesday, 10, 0));
    }

    // ---------- Attendance: gym_closed en todas las vías ----------

    [Fact]
    public async Task Member_qr_checkin_with_the_gym_closed_is_rejected_before_looking_at_the_membership()
    {
        // Sin membresía: si se mirara primero, el mensaje hablaría de la membresía.
        var w = await SeedMemberAsync(withMembership: false);
        await SaveWeekAsync(w.GymId);
        var night = new DateTimeOffset(2026, 10, 6, 5, 0, 0, TimeSpan.Zero); // martes 00:00 local: cerrado hasta las 5:00

        var ex = await Assert.ThrowsAsync<ConflictException>(() => CheckInMemberAsync(w, night));

        Assert.Contains("cerrado", ex.Message);
        Assert.Contains("Abre", ex.Message);
        Assert.Equal(0, await AttendanceCountAsync(w));
    }

    [Fact]
    public async Task Reception_cannot_let_a_member_in_when_the_gym_is_closed_even_with_a_reason()
    {
        var w = await SeedMemberAsync(withMembership: true);
        await SaveWeekAsync(w.GymId);
        var night = new DateTimeOffset(2026, 10, 6, 5, 0, 0, TimeSpan.Zero);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => CheckInStaffAsync(w, night, "Es un caso especial"));

        Assert.Contains("cerrado", ex.Message);
        Assert.Equal(0, await AttendanceCountAsync(w));
    }

    [Fact]
    public async Task Open_gym_lets_both_ways_through_and_a_gym_without_schedule_never_rejects()
    {
        var open = await SeedMemberAsync(withMembership: true);
        await SaveWeekAsync(open.GymId);
        var morning = new DateTimeOffset(2026, 10, 5, 14, 0, 0, TimeSpan.Zero); // lunes 09:00 local

        Assert.True((await CheckInStaffAsync(open, morning, null)).Created);

        var none = await SeedMemberAsync(withMembership: true);
        Assert.True((await CheckInMemberAsync(none, new DateTimeOffset(2026, 10, 6, 7, 0, 0, TimeSpan.Zero))).Created);
    }

    [Fact]
    public async Task Class_attendance_is_rejected_when_the_gym_is_closed_that_day_and_works_otherwise()
    {
        var w = await SeedMemberAsync(withMembership: true);
        await SaveWeekAsync(w.GymId);
        var classId = await CreateClassAsync(w); // lunes 7:00

        var closedDay = Monday;
        await UpsertSpecialAsync(w.GymId, closedDay, closed: true, []);

        using (var scope = Staff(w, closedDay, 12))
        {
            var ex = await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<MarkAttendanceHandler>()
                .HandleAsync(classId, closedDay, new MarkAttendanceRequest([w.MemberId], null), default));
            Assert.Contains("cerrado", ex.Message);
        }

        Assert.Equal(0, await AttendanceCountAsync(w));

        var openDay = Monday.AddDays(14); // 2026-10-19
        using (var scope = Staff(w, openDay, 12))
        {
            await scope.ServiceProvider.GetRequiredService<MarkAttendanceHandler>()
                .HandleAsync(classId, openDay, new MarkAttendanceRequest([w.MemberId], null), default);
        }

        Assert.Equal(1, await AttendanceCountAsync(w));
    }

    // ---------- Cerrar un día exige confirmación; HTTP ----------

    [Fact]
    public async Task Closing_a_day_without_confirmation_is_a_422_and_with_it_works()
    {
        var gymId = await SeedGymAsync();
        await SaveWeekAsync(gymId);
        await using var host = RealIdentityHost();
        var owner = Client(host, gymId, UserRole.Owner);

        var date = DateTime.UtcNow.AddMonths(6).ToString("yyyy-MM-dd");
        var without = await owner.PutAsJsonAsync($"/api/gym/special-days/{date}",
            new { isClosed = true, confirmClosed = false, slots = (object?)null, note = "Arreglo" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, without.StatusCode);

        var with = await owner.PutAsJsonAsync($"/api/gym/special-days/{date}",
            new { isClosed = true, confirmClosed = true, slots = (object?)null, note = "Arreglo" });
        Assert.Equal(HttpStatusCode.OK, with.StatusCode);

        var list = await owner.GetFromJsonAsync<JsonElement>("/api/gym/special-days");
        Assert.Single(list.EnumerateArray());
    }

    [Fact]
    public async Task Opening_hours_endpoint_validates_shape_and_business_rules_and_is_owner_only()
    {
        var gymId = await SeedGymAsync();
        await using var host = RealIdentityHost();
        var owner = Client(host, gymId, UserRole.Owner);
        var staff = Client(host, gymId, UserRole.Staff);

        // Formato de hora inválido: error de forma, 400.
        var bad = Request(mon: [("5am", "12:00")]);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/gym/opening-hours", bad)).StatusCode);

        // Cierre antes de la apertura: regla de negocio, 422.
        var inverted = Request(mon: [("12:00", "05:00")]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.PutAsJsonAsync("/api/gym/opening-hours", inverted)).StatusCode);

        // Un día sin tramos: 422.
        var hole = Request(tue: []);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.PutAsJsonAsync("/api/gym/opening-hours", hole)).StatusCode);

        // Falta el horario de festivos: 422.
        var withoutHoliday = new { days = Request().days.Where(d => d.day != "Holiday").ToArray() };
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await owner.PutAsJsonAsync("/api/gym/opening-hours", withoutHoliday)).StatusCode);

        // El staff lee pero no escribe.
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync("/api/gym/opening-hours", Request())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/gym/opening-hours")).StatusCode);

        // Y el válido se guarda y se lee.
        var ok = await owner.PutAsJsonAsync("/api/gym/opening-hours", Request());
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var hours = await owner.GetFromJsonAsync<JsonElement>("/api/gym/opening-hours");
        Assert.True(hours.GetProperty("hasSchedule").GetBoolean());
        Assert.Equal(8, hours.GetProperty("days").GetArrayLength());
    }

    [Fact]
    public async Task Holidays_list_shows_the_holiday_schedule_and_the_adjustment_and_the_member_reads_the_week()
    {
        var gymId = await SeedGymAsync();
        await using var host = RealIdentityHost();
        var owner = Client(host, gymId, UserRole.Owner);
        var member = Client(host, gymId, UserRole.Member);
        await owner.PutAsJsonAsync("/api/gym/opening-hours", Request());

        var date = ColombianHolidays.ForYear(DateTime.UtcNow.Year + 1)[0].Date;
        var put = await owner.PutAsJsonAsync($"/api/gym/special-days/{date:yyyy-MM-dd}",
            new { isClosed = false, confirmClosed = false, slots = new[] { new { opens = "09:00", closes = "11:00" } }, note = "Ajustado" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var holidays = await owner.GetFromJsonAsync<JsonElement>($"/api/gym/holidays?year={date.Year}");
        Assert.Equal(18, holidays.GetArrayLength());
        var first = holidays.EnumerateArray().First();
        Assert.True(first.GetProperty("isAdjusted").GetBoolean());
        Assert.Equal("09:00", first.GetProperty("slots")[0].GetProperty("opens").GetString());
        var second = holidays.EnumerateArray().ElementAt(1);
        Assert.False(second.GetProperty("isAdjusted").GetBoolean());
        Assert.Equal("07:00", second.GetProperty("slots")[0].GetProperty("opens").GetString());

        var hours = await member.GetFromJsonAsync<JsonElement>("/api/me/gym/hours");
        Assert.Equal(8, hours.GetProperty("days").GetArrayLength());
        Assert.True(hours.GetProperty("status").GetProperty("hasSchedule").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(hours.GetProperty("status").GetProperty("text").GetString()));
    }

    // ---------- Soporte ----------

    private static IReadOnlyDictionary<OpeningDay, IReadOnlyList<TimeSlot>> Week()
    {
        TimeSlot S(string a, string b) => new(TimeOnly.Parse(a), TimeOnly.Parse(b));
        var weekdays = new[] { S("05:00", "12:00"), S("15:00", "22:00") };
        return new Dictionary<OpeningDay, IReadOnlyList<TimeSlot>>
        {
            [OpeningDay.Monday] = weekdays,
            [OpeningDay.Tuesday] = [S("05:00", "22:00")],
            [OpeningDay.Wednesday] = [S("05:00", "22:00")],
            [OpeningDay.Thursday] = [S("05:00", "22:00")],
            [OpeningDay.Friday] = [S("05:00", "22:00")],
            [OpeningDay.Saturday] = [S("07:00", "14:00")],
            [OpeningDay.Sunday] = [S("07:00", "13:00")],
            [OpeningDay.Holiday] = [S("07:00", "13:00")],
        };
    }

    /// <summary>Semana de prueba: lunes partido 5-12 y 15-22; martes a viernes 5-22; sábado 7-14; domingo y festivos 7-13.</summary>
    private static UpdateOpeningHoursRequest Typed(
        (string, string)[]? mon = null, (string, string)[]? tue = null)
    {
        OpeningDayDto D(string day, params (string, string)[] s)
            => new(day, s.Select(t => new TimeSlotDto(t.Item1, t.Item2)).ToList());

        return new UpdateOpeningHoursRequest(
        [
            D("Monday", mon ?? [("05:00", "12:00"), ("15:00", "22:00")]),
            D("Tuesday", tue ?? [("05:00", "22:00")]),
            D("Wednesday", ("05:00", "22:00")),
            D("Thursday", ("05:00", "22:00")),
            D("Friday", ("05:00", "22:00")),
            D("Saturday", ("07:00", "14:00")),
            D("Sunday", ("07:00", "13:00")),
            D("Holiday", ("07:00", "13:00")),
        ]);
    }

    private static HoursBody Request((string, string)[]? mon = null, (string, string)[]? tue = null)
    {
        (string day, object[] slots) D(string day, params (string, string)[] s)
            => (day, s.Select(t => (object)new { opens = t.Item1, closes = t.Item2 }).ToArray());

        var days = new[]
        {
            D("Monday", mon ?? [("05:00", "12:00"), ("15:00", "22:00")]),
            D("Tuesday", tue ?? [("05:00", "22:00")]),
            D("Wednesday", ("05:00", "22:00")),
            D("Thursday", ("05:00", "22:00")),
            D("Friday", ("05:00", "22:00")),
            D("Saturday", ("07:00", "14:00")),
            D("Sunday", ("07:00", "13:00")),
            D("Holiday", ("07:00", "13:00")),
        }.Select(t => new DayBody(t.day, t.slots)).ToArray();
        return new HoursBody(days);
    }

    private sealed record DayBody(string day, object[] slots);

    private sealed record HoursBody(DayBody[] days);

    private WebApplicationFactory<Program> RealIdentityHost()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, CurrentUser>();
        }));

    private static HttpClient Client(WebApplicationFactory<Program> host, Guid gymId, UserRole role)
    {
        var token = host.Services.GetRequiredService<IJwtTokenService>().CreateAccessToken(
            new AuthenticatedUser(Guid.CreateVersion7(), "horario@test.local", "Horario", role, gymId));
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        return client;
    }

    private IServiceScope Owner(Guid gymId, DateOnly today)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), gymId, UserRole.Owner);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(today);
        return scope;
    }

    private IServiceScope Staff(World w, DateOnly day, int hour)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), w.GymId, UserRole.Staff);
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
        clock.PinTo(clock.BusinessDayRange(day).Start.AddHours(hour), day);
        return scope;
    }

    private async Task SaveWeekAsync(
        Guid gymId, (string, string)[]? mon = null, (string, string)[]? tue = null)
    {
        using var scope = Owner(gymId, Monday);
        await scope.ServiceProvider.GetRequiredService<UpdateOpeningHoursHandler>()
            .HandleAsync(Typed(mon, tue), default);
    }

    private async Task UpsertSpecialAsync(Guid gymId, DateOnly date, bool closed, (string, string)[] slots)
    {
        using var scope = Owner(gymId, Monday);
        await scope.ServiceProvider.GetRequiredService<UpsertSpecialDayHandler>().HandleAsync(
            date,
            new UpsertSpecialDayRequest(
                closed, closed,
                slots.Select(s => new TimeSlotDto(s.Item1.Contains(':') ? s.Item1 : $"{int.Parse(s.Item1):00}:00",
                    s.Item2.Contains(':') ? s.Item2 : $"{int.Parse(s.Item2):00}:00")).ToList(),
                null),
            default);
    }

    private async Task<GymOpenStatus> StatusAsync(Guid gymId, DateOnly day, int hour, int minute)
    {
        using var scope = factory.CreateScopeWithMigration();
        return await scope.ServiceProvider.GetRequiredService<IGymReader>()
            .GetOpenStatusAtLocalAsync(gymId, day, new TimeOnly(hour, minute));
    }

    private async Task<bool> IsOpenAsync(Guid gymId, DateOnly day, int hour, int minute)
        => (await StatusAsync(gymId, day, hour, minute)).IsOpen;

    private async Task<Guid> SeedGymAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var gym = Gym.Create("Horario Gym", $"hor-{Guid.NewGuid():N}"[..20]);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();
        return gym.Id;
    }

    private async Task<World> SeedMemberAsync(bool withMembership)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Horario Checkin", $"hc-{Guid.NewGuid():N}"[..16]);
        var userId = Guid.CreateVersion7();
        var member = Member.Create(gym.Id, "Ana", "Horario", $"doc-{Guid.NewGuid():N}"[..12], new DateOnly(2026, 1, 1));
        member.LinkUser(userId);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), member);

        if (withMembership)
        {
            var plan = MembershipPlan.Create(gym.Id, "Mensual", null, 50000m, 90);
            db.AddRange(plan, Membership.Create(gym.Id, member.Id, plan.Id, new DateOnly(2026, 9, 1), 90));
        }

        await db.SaveChangesAsync();
        return new World(gym.Id, member.Id, userId);
    }

    private IServiceScope AtInstant(World w, UserRole role, DateTimeOffset utc)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>()
            .ActAs(role == UserRole.Member ? w.UserId : Guid.CreateVersion7(), w.GymId, role);
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
        clock.PinTo(utc, DateOnly.FromDateTime(clock.ToBusinessLocal(utc)));
        return scope;
    }

    private async Task<(CheckInResponse Response, bool Created)> CheckInMemberAsync(World w, DateTimeOffset utc)
    {
        using var scope = AtInstant(w, UserRole.Member, utc);
        var code = scope.ServiceProvider.GetRequiredService<ICheckinCodeSigner>()
            .Generate(w.GymId, CheckinMode.Rotating, 1, utc);
        return await scope.ServiceProvider.GetRequiredService<CheckInHandler>()
            .HandleAsync(new CheckInRequest(null, code), default);
    }

    private async Task<(CheckInResponse Response, bool Created)> CheckInStaffAsync(
        World w, DateTimeOffset utc, string? reason)
    {
        using var scope = AtInstant(w, UserRole.Staff, utc);
        return await scope.ServiceProvider.GetRequiredService<CheckInHandler>()
            .HandleAsync(new CheckInRequest(w.MemberId, null, reason), default);
    }

    private async Task<int> AttendanceCountAsync(World w)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>()
            .Set<AttendanceEntity>().CountAsync(a => a.MemberId == w.MemberId);
    }

    private async Task<Guid> CreateClassAsync(World w)
    {
        using var scope = Staff(w, Monday, 8);
        var created = await scope.ServiceProvider.GetRequiredService<CreateClassHandler>().HandleAsync(
            new SaveClassRequest(
                "Spinning", null, "Ana", null, new TimeOnly(7, 0), 45,
                IsRecurring: true, DaysOfWeek: [ClassDayOfWeek.Monday], ScheduledDate: null), default);
        return created.Id;
    }

    private sealed record World(Guid GymId, Guid MemberId, Guid UserId);
}
