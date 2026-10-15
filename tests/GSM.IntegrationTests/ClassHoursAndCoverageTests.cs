using GSM.Application.Classes;
using GSM.Application.Classes.Contracts;
using GSM.Application.Gyms;
using GSM.Application.Gyms.Contracts;
using GSM.Domain.Attendance;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;

namespace GSM.IntegrationTests;

/// <summary>
/// Classes v2, C1: las clases solo se agendan, se reservan y se asisten dentro del horario del gym, y
/// reservar o asistir exige estar al día el día de la sesión (sin "Dejar pasar").
/// </summary>
public sealed class ClassHoursAndCoverageTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Monday = new(2026, 3, 2);
    private static readonly DateOnly NextMonday = Monday.AddDays(7);

    // ---------- Horario del gym al crear y editar ----------

    [Fact]
    public async Task Creating_a_recurring_class_outside_the_weekly_hours_is_422_and_inside_works()
    {
        var w = await SeedAsync(1);
        await SaveWeekAsync(w.GymId); // lunes partido 6-12 y 15-20; resto 6-20; festivos 7-13

        // Madruga: abre a las 6:00.
        var ex = await Assert.ThrowsAsync<ValidationDomainException>(
            () => CreateAsync(w, new TimeOnly(5, 30), 45, [ClassDayOfWeek.Monday]));
        Assert.Contains("fuera del horario", ex.Message);
        Assert.Contains("lunes", ex.Message);

        // Cabe en el primer tramo.
        var ok = await CreateAsync(w, new TimeOnly(7, 0), 45, [ClassDayOfWeek.Monday]);
        Assert.NotEqual(Guid.Empty, ok.Id);
    }

    [Fact]
    public async Task A_class_that_ends_after_closing_or_spans_the_split_gap_is_422()
    {
        var w = await SeedAsync(1);
        await SaveWeekAsync(w.GymId);

        // Martes cierra a las 20:00: 19:30 + 45 min se pasa.
        await Assert.ThrowsAsync<ValidationDomainException>(
            () => CreateAsync(w, new TimeOnly(19, 30), 45, [ClassDayOfWeek.Tuesday]));

        // El lunes cierra a las 12:00 y reabre a las 15:00: 11:30 + 60 min cae en el hueco.
        await Assert.ThrowsAsync<ValidationDomainException>(
            () => CreateAsync(w, new TimeOnly(11, 30), 60, [ClassDayOfWeek.Monday]));

        // Todos los días de la serie se miden: martes cabe, lunes no (5:30).
        await Assert.ThrowsAsync<ValidationDomainException>(
            () => CreateAsync(w, new TimeOnly(5, 30), 30, [ClassDayOfWeek.Tuesday, ClassDayOfWeek.Monday]));
    }

    [Fact]
    public async Task Editing_a_class_to_a_time_outside_the_hours_is_422()
    {
        var w = await SeedAsync(1);
        await SaveWeekAsync(w.GymId);
        var created = await CreateAsync(w, new TimeOnly(7, 0), 45, [ClassDayOfWeek.Monday]);

        using var scope = Staff(w);
        await Assert.ThrowsAsync<ValidationDomainException>(() => scope.ServiceProvider
            .GetRequiredService<UpdateClassHandler>()
            .HandleAsync(created.Id, Request(new TimeOnly(21, 0), 45, [ClassDayOfWeek.Monday]), default));
    }

    [Fact]
    public async Task A_one_off_class_is_measured_against_its_date_including_closing_exceptions()
    {
        var w = await SeedAsync(1);
        await SaveWeekAsync(w.GymId);
        await UpsertSpecialAsync(w.GymId, NextMonday, closed: true, []);

        var ex = await Assert.ThrowsAsync<ValidationDomainException>(() => CreateOneOffAsync(w, NextMonday));
        Assert.Contains("fuera del horario", ex.Message);

        var ok = await CreateOneOffAsync(w, NextMonday.AddDays(1));
        Assert.NotEqual(Guid.Empty, ok.Id);
    }

    [Fact]
    public async Task A_class_at_2am_is_valid_on_a_24_hour_day_and_422_on_a_normal_day()
    {
        var w = await SeedAsync(1);
        using (var scope = Owner(w.GymId))
        {
            OpeningDayDto D(string day, bool all = false)
                => all
                    ? new OpeningDayDto(day, []) { Is24Hours = true }
                    : new OpeningDayDto(day, [new TimeSlotDto("06:00", "20:00")]);

            await scope.ServiceProvider.GetRequiredService<UpdateOpeningHoursHandler>().HandleAsync(
                new UpdateOpeningHoursRequest(
                [
                    D("Monday"), D("Tuesday", all: true), D("Wednesday"), D("Thursday"),
                    D("Friday"), D("Saturday"), D("Sunday"), D("Holiday"),
                ]), default);
        }

        var ok = await CreateAsync(w, new TimeOnly(2, 0), 45, [ClassDayOfWeek.Tuesday]);
        Assert.NotEqual(Guid.Empty, ok.Id);

        await Assert.ThrowsAsync<ValidationDomainException>(
            () => CreateAsync(w, new TimeOnly(2, 0), 45, [ClassDayOfWeek.Monday]));
    }

    [Fact]
    public async Task A_gym_without_a_schedule_accepts_any_class()
    {
        var w = await SeedAsync(1);

        var created = await CreateAsync(w, new TimeOnly(4, 0), 90, [ClassDayOfWeek.Sunday]);

        Assert.NotEqual(Guid.Empty, created.Id);
    }

    // ---------- Ocurrencias fuera del horario ----------

    [Fact]
    public async Task Booking_an_occurrence_on_a_closed_day_is_409_and_materializes_nothing()
    {
        var w = await SeedAsync(1);
        await SaveWeekAsync(w.GymId);
        var created = await CreateAsync(w, new TimeOnly(7, 0), 45, [ClassDayOfWeek.Monday], capacity: 10);

        await UpsertSpecialAsync(w.GymId, NextMonday, closed: true, []);

        using (var scope = AsMember(w, 0))
        {
            var ex = await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<BookClassHandler>().HandleAsync(created.Id, NextMonday, default));
            Assert.Contains("Gym cerrado", ex.Message);
        }

        using (var scope = Staff(w))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            Assert.Equal(0, await db.Set<ClassSession>().CountAsync(s => s.ClassId == created.Id));
        }
    }

    [Fact]
    public async Task A_holiday_with_a_shorter_schedule_blocks_booking_and_the_schedule_marks_it_as_gym_closed()
    {
        var w = await SeedAsync(1);
        await SaveWeekAsync(w.GymId);
        var created = await CreateAsync(w, new TimeOnly(7, 0), 45, [ClassDayOfWeek.Monday], capacity: 10);

        // Excepción puntual con horario corto (8-9): la clase de las 7:00 queda fuera.
        await UpsertSpecialAsync(w.GymId, NextMonday, closed: false, [("08:00", "09:00")]);

        using (var scope = AsMember(w, 0))
        {
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<BookClassHandler>().HandleAsync(created.Id, NextMonday, default));

            var schedule = await scope.ServiceProvider.GetRequiredService<GetMyClassesHandler>()
                .HandleAsync(NextMonday, NextMonday, default);
            Assert.True(schedule.Entries.Single().OutsideOpeningHours);
        }

        // C3: la ventana de reserva por defecto (7 días) no llega a la semana siguiente; se amplía.
        using (var scope = Owner(w.GymId))
        {
            await scope.ServiceProvider.GetRequiredService<UpdateClassSettingsHandler>()
                .HandleAsync(new UpdateClassSettingsRequest(2, 3, 30, 7, 30, 5), default);
        }

        // Otra semana sigue reservable y sin marca.
        using (var scope = AsMember(w, 0))
        {
            var r = await scope.ServiceProvider.GetRequiredService<BookClassHandler>()
                .HandleAsync(created.Id, NextMonday.AddDays(7), default);
            Assert.Equal(ClassBookingStatus.Booked.ToString(), r.Status);

            var schedule = await scope.ServiceProvider.GetRequiredService<GetMyClassesHandler>()
                .HandleAsync(NextMonday.AddDays(7), NextMonday.AddDays(7), default);
            Assert.False(schedule.Entries.Single().OutsideOpeningHours);
        }
    }

    [Fact]
    public async Task Closing_the_gym_on_a_day_with_bookings_cancels_the_session_and_its_bookings_when_staff_opens_the_schedule()
    {
        var w = await SeedAsync(2);
        await SaveWeekAsync(w.GymId);
        var created = await CreateAsync(w, new TimeOnly(7, 0), 45, [ClassDayOfWeek.Monday], capacity: 10);

        foreach (var i in new[] { 0, 1 })
        {
            using var scope = AsMember(w, i);
            await scope.ServiceProvider.GetRequiredService<BookClassHandler>()
                .HandleAsync(created.Id, NextMonday, default);
        }

        await UpsertSpecialAsync(w.GymId, NextMonday, closed: true, []);

        using (var scope = Staff(w))
        {
            var schedule = await scope.ServiceProvider.GetRequiredService<GetScheduleHandler>()
                .HandleAsync(NextMonday, NextMonday, default);

            var entry = Assert.Single(schedule.Entries);
            Assert.Equal(ClassSessionStatus.Cancelled.ToString(), entry.Status);
            Assert.Equal("Gym cerrado", entry.CancellationReason);
        }

        using (var scope = Staff(w))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var bookings = await db.Set<ClassBooking>().ToListAsync();
            Assert.Equal(2, bookings.Count);
            Assert.All(bookings, b => Assert.Equal(ClassBookingStatus.Cancelled, b.Status));
        }
    }

    [Fact]
    public async Task Marking_attendance_while_the_gym_is_closed_is_409_even_with_a_covered_member()
    {
        var w = await SeedAsync(1);
        await SaveWeekAsync(w.GymId);
        var created = await CreateAsync(w, new TimeOnly(7, 0), 45, [ClassDayOfWeek.Monday], capacity: 10);
        await UpsertSpecialAsync(w.GymId, NextMonday, closed: true, []);

        using var scope = Staff(w);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
            .GetRequiredService<MarkAttendanceHandler>()
            .HandleAsync(created.Id, NextMonday, new MarkAttendanceRequest([w.MemberIds[0]], null), default));
        Assert.Contains("cerrado", ex.Message);
    }

    [Fact]
    public async Task Changing_the_gym_hours_lists_the_affected_classes_without_blocking_the_change()
    {
        var w = await SeedAsync(1);
        var created = await CreateAsync(w, new TimeOnly(7, 0), 45, [ClassDayOfWeek.Monday, ClassDayOfWeek.Tuesday]);

        // Sin horario cargado nada queda fuera.
        using (var scope = Staff(w))
        {
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<ListClassesOutsideHoursHandler>()
                .HandleAsync(default));
        }

        // El gym pasa a abrir a las 9:00 los lunes: el cambio se acepta y la clase aparece en la lista.
        await SaveWeekAsync(w.GymId, mondaySlots: [("09:00", "20:00")]);

        using (var scope = Staff(w))
        {
            var item = Assert.Single(await scope.ServiceProvider
                .GetRequiredService<ListClassesOutsideHoursHandler>().HandleAsync(default));
            Assert.Equal(created.Id, item.ClassId);
            Assert.Equal(["Monday"], item.DaysOutside);
        }
    }

    // ---------- Estar al día ----------

    [Fact]
    public async Task Booking_requires_coverage_on_the_session_day_with_the_renew_message()
    {
        // Miembro 0: sin membresía. 1: vence antes de la clase y fuera de la gracia. 2: cubierto.
        var w = await SeedAsync(3, coverage: new Dictionary<int, (DateOnly Start, int Days)>
        {
            [1] = (Monday.AddDays(-30), 30),   // termina el 2026-03-01, hace más de 3 días de gracia al 9-mar
            [2] = (Monday.AddDays(-5), 90),
        });
        var created = await CreateAsync(w, new TimeOnly(7, 0), 45, [ClassDayOfWeek.Monday], capacity: 10);

        using (var scope = AsMember(w, 0))
        {
            var ex = await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<BookClassHandler>().HandleAsync(created.Id, NextMonday, default));
            Assert.Contains("renueva", ex.Message);
        }

        using (var scope = AsMember(w, 1))
        {
            var ex = await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<BookClassHandler>().HandleAsync(created.Id, NextMonday, default));
            Assert.Contains("Tu plan vence el 1 mar", ex.Message);
            Assert.Contains("renueva para reservar esta clase", ex.Message);
        }

        using (var scope = AsMember(w, 2))
        {
            var r = await scope.ServiceProvider.GetRequiredService<BookClassHandler>()
                .HandleAsync(created.Id, NextMonday, default);
            Assert.Equal(ClassBookingStatus.Booked.ToString(), r.Status);
            Assert.False(r.MembershipExpired);
        }

        // Un rechazo no deja reserva alguna.
        using (var scope = Staff(w))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            Assert.Equal(1, await db.Set<ClassBooking>().CountAsync());
        }
    }

    [Fact]
    public async Task A_plan_that_expires_before_the_class_but_within_the_grace_can_still_book_with_a_notice()
    {
        // Termina el domingo 8-mar; la clase es el lunes 9-mar: un día de gracia (3 por defecto).
        var w = await SeedAsync(1, coverage: new Dictionary<int, (DateOnly Start, int Days)>
        {
            [0] = (NextMonday.AddDays(-1 - 29), 30),
        });
        var created = await CreateAsync(w, new TimeOnly(7, 0), 45, [ClassDayOfWeek.Monday], capacity: 10);

        using var scope = AsMember(w, 0);
        var r = await scope.ServiceProvider.GetRequiredService<BookClassHandler>()
            .HandleAsync(created.Id, NextMonday, default);

        Assert.Equal(ClassBookingStatus.Booked.ToString(), r.Status);
        Assert.True(r.MembershipExpired);
        Assert.NotNull(r.AccessNotice);
    }

    [Fact]
    public async Task A_frozen_membership_cannot_book()
    {
        var w = await SeedAsync(1, coverage: new Dictionary<int, (DateOnly Start, int Days)>
        {
            [0] = (Monday.AddDays(-5), 90),
        });
        await FreezeMembershipAsync(w, 0);
        var created = await CreateAsync(w, new TimeOnly(7, 0), 45, [ClassDayOfWeek.Monday], capacity: 10);

        using var scope = AsMember(w, 0);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
            .GetRequiredService<BookClassHandler>().HandleAsync(created.Id, NextMonday, default));
        Assert.Contains("congelada", ex.Message);
    }

    [Fact]
    public async Task Marking_attendance_for_a_member_who_is_not_up_to_date_is_409_without_letting_through()
    {
        // Miembro 0 cubierto, 1 vencido fuera de la gracia.
        var w = await SeedAsync(2, coverage: new Dictionary<int, (DateOnly Start, int Days)>
        {
            [0] = (Monday.AddDays(-5), 90),
            [1] = (Monday.AddDays(-60), 30),
        });
        var created = await CreateAsync(w, new TimeOnly(7, 0), 45, [ClassDayOfWeek.Monday], capacity: null);

        using (var scope = Staff(w))
        {
            // El lote entero se rechaza: nadie queda marcado ni con entrada del día.
            var ex = await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<MarkAttendanceHandler>()
                .HandleAsync(created.Id, Monday,
                    new MarkAttendanceRequest([w.MemberIds[0], w.MemberIds[1]], null), default));
            Assert.Contains("Debe renovar", ex.Message);
        }

        using (var scope = Staff(w))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            Assert.Equal(0, await db.Set<ClassBooking>().CountAsync());
            Assert.Equal(0, await db.Set<AttendanceEntity>().CountAsync());
        }

        // Solo el miembro al día sí puede.
        using (var scope = Staff(w))
        {
            var roster = await scope.ServiceProvider.GetRequiredService<MarkAttendanceHandler>()
                .HandleAsync(created.Id, Monday, new MarkAttendanceRequest([w.MemberIds[0]], null), default);
            Assert.Equal(ClassBookingStatus.Attended.ToString(), roster.Entries.Single().Status);
        }
    }

    [Fact]
    public async Task A_booking_made_while_covered_cannot_be_attended_once_the_plan_lapses()
    {
        // Plan del 25-feb al 4-mar: la sesión del 9-mar ya está fuera de la gracia (hasta el 7-mar).
        var w = await SeedAsync(1, coverage: new Dictionary<int, (DateOnly Start, int Days)>
        {
            [0] = (Monday.AddDays(-5), 8),
        });
        var created = await CreateAsync(w, new TimeOnly(7, 0), 45, [ClassDayOfWeek.Monday], capacity: 10);

        // Reservar hoy para el 9-mar ya se rechaza...
        using (var scope = AsMember(w, 0))
        {
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<BookClassHandler>().HandleAsync(created.Id, NextMonday, default));
        }

        // ...así que la reserva "tomada cuando estaba cubierto" se siembra directo.
        Guid sessionId;
        using (var scope = Staff(w))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var session = ClassSession.Materialize(w.GymId, created.Id, NextMonday, new TimeOnly(7, 0), 45, "Ana", 10);
            db.Add(session);
            db.Add(ClassBooking.Book(w.GymId, session.Id, w.MemberIds[0], DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            sessionId = session.Id;
        }

        using (var scope = Staff(w))
        {
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<MarkAttendanceHandler>()
                .HandleAsync(created.Id, NextMonday, new MarkAttendanceRequest([w.MemberIds[0]], null), default));
        }

        // La reserva sigue ahí: no se castiga ni se borra.
        using (var scope = Staff(w))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var booking = await db.Set<ClassBooking>().SingleAsync(b => b.SessionId == sessionId);
            Assert.Equal(ClassBookingStatus.Booked, booking.Status);
        }
    }

    // ---------- Siembra y ayudas ----------

    private async Task<ClassResponse> CreateAsync(
        World w, TimeOnly start, int minutes, ClassDayOfWeek[] days, int? capacity = 10)
    {
        using var scope = Staff(w);
        return await scope.ServiceProvider.GetRequiredService<CreateClassHandler>()
            .HandleAsync(Request(start, minutes, days, capacity), default);
    }

    private static SaveClassRequest Request(TimeOnly start, int minutes, ClassDayOfWeek[] days, int? capacity = 10)
        => new("Spinning", null, "Ana", capacity, start, minutes,
            IsRecurring: true, DaysOfWeek: days, ScheduledDate: null);

    private async Task<ClassResponse> CreateOneOffAsync(World w, DateOnly date)
    {
        using var scope = Staff(w);
        return await scope.ServiceProvider.GetRequiredService<CreateClassHandler>().HandleAsync(
            new SaveClassRequest("Taller", null, null, 10, new TimeOnly(7, 0), 45,
                IsRecurring: false, DaysOfWeek: null, ScheduledDate: date), default);
    }

    /// <summary>Lunes partido 6-12 y 15-20 (o <paramref name="mondaySlots"/>); resto 6-20; festivos 7-13.</summary>
    private async Task SaveWeekAsync(Guid gymId, (string, string)[]? mondaySlots = null)
    {
        OpeningDayDto D(string day, params (string, string)[] s)
            => new(day, s.Select(t => new TimeSlotDto(t.Item1, t.Item2)).ToList());

        using var scope = Owner(gymId);
        await scope.ServiceProvider.GetRequiredService<UpdateOpeningHoursHandler>().HandleAsync(
            new UpdateOpeningHoursRequest(
            [
                D("Monday", mondaySlots ?? [("06:00", "12:00"), ("15:00", "20:00")]),
                D("Tuesday", ("06:00", "20:00")),
                D("Wednesday", ("06:00", "20:00")),
                D("Thursday", ("06:00", "20:00")),
                D("Friday", ("06:00", "20:00")),
                D("Saturday", ("06:00", "20:00")),
                D("Sunday", ("06:00", "20:00")),
                D("Holiday", ("07:00", "13:00")),
            ]), default);
    }

    private async Task UpsertSpecialAsync(Guid gymId, DateOnly date, bool closed, (string, string)[] slots)
    {
        using var scope = Owner(gymId);
        await scope.ServiceProvider.GetRequiredService<UpsertSpecialDayHandler>().HandleAsync(
            date,
            new UpsertSpecialDayRequest(
                closed, closed, slots.Select(s => new TimeSlotDto(s.Item1, s.Item2)).ToList(), null),
            default);
    }

    private async Task FreezeMembershipAsync(World w, int memberIndex)
    {
        using var scope = Staff(w);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var membership = await db.Set<Membership>().SingleAsync(m => m.MemberId == w.MemberIds[memberIndex]);
        db.Add(MembershipFreeze.Create(membership.GymId, membership.Id, Monday, null, "Prueba", true, 0, null));
        await db.SaveChangesAsync();
    }

    private IServiceScope Owner(Guid gymId)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), gymId, UserRole.Owner);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Monday);
        return scope;
    }

    private IServiceScope Staff(World w)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.StaffId, w.GymId, UserRole.Staff);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Monday);
        return scope;
    }

    private IServiceScope AsMember(World w, int index)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.MemberUserIds[index], w.GymId, UserRole.Member);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Monday);
        return scope;
    }

    /// <summary>Un gym con <paramref name="members"/> miembros; <paramref name="coverage"/> da su membresía (el resto no tiene).</summary>
    private async Task<World> SeedAsync(
        int members, Dictionary<int, (DateOnly Start, int Days)>? coverage = null)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Monday);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, attendance.attendances, achievements.achievements, achievements.member_achievements restart identity cascade;");
        await db.Database.ExecuteSqlRawAsync("delete from classes.gym_classes;");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("CH", $"ch-{suffix}");
        var staff = MakeUser($"ch-{suffix}-staff");
        gym.AssignOwner(staff.Id);
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);

        coverage ??= Enumerable.Range(0, members).ToDictionary(i => i, _ => (Monday.AddDays(-5), 90));
        var plan = MembershipPlan.Create(gym.Id, "Mensual", null, 50000m, 30);
        db.Add(plan);

        var memberIds = new List<Guid>();
        var memberUserIds = new List<Guid>();
        for (var i = 0; i < members; i++)
        {
            var u = MakeUser($"ch-{suffix}-m{i}");
            var m = Member.Create(gym.Id, "Miembro", $"{i}", $"doc-{suffix}-{i}", new DateOnly(2026, 1, 1));
            m.LinkUser(u.Id);
            db.Add(u);
            db.Add(m);
            if (coverage.TryGetValue(i, out var c))
            {
                db.Add(Membership.Create(gym.Id, m.Id, plan.Id, c.Start, c.Days));
            }

            memberIds.Add(m.Id);
            memberUserIds.Add(u.Id);
        }

        await db.SaveChangesAsync();
        return new World(gym.Id, staff.Id, memberIds, memberUserIds);
    }

    private static GSM.Infrastructure.Identity.AppUser MakeUser(string local)
        => new()
        {
            UserName = $"{local}@test.local",
            Email = $"{local}@test.local",
            NormalizedUserName = $"{local}@TEST.LOCAL",
            NormalizedEmail = $"{local}@TEST.LOCAL",
            EmailConfirmed = true,
            FullName = local,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };

    private sealed record World(Guid GymId, Guid StaffId, IReadOnlyList<Guid> MemberIds, IReadOnlyList<Guid> MemberUserIds);
}
