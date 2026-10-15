using GSM.Application.Classes;
using GSM.Application.Classes.Contracts;
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

namespace GSM.IntegrationTests;

/// <summary>
/// Classes v2, C3: reglas de reserva configurables por gym: plazo de cancelación, ventana de reserva,
/// máximo de reservas activas y penalización por no-show.
/// </summary>
public sealed class ClassBookingRulesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Monday = new(2026, 3, 2);
    private static readonly DateOnly NextMonday = Monday.AddDays(7);
    private static readonly DateOnly NextTuesday = NextMonday.AddDays(1);
    private static readonly DateOnly NextWednesday = NextMonday.AddDays(2);

    // ---------- Ajustes perezosos ----------

    [Fact]
    public async Task Without_a_row_the_defaults_apply_and_the_first_update_creates_it()
    {
        var w = await SeedAsync(1);

        using (var scope = Owner(w))
        {
            var s = await scope.ServiceProvider.GetRequiredService<GetClassSettingsHandler>().HandleAsync(default);
            Assert.Equal(new ClassSettingsResponse(2, 3, 30, 7, 7, 5, 60), s);

            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            Assert.Equal(0, await db.Set<ClassSettings>().CountAsync());
        }

        await UpdateSettingsAsync(w, new UpdateClassSettingsRequest(4, 2, 15, 10, 14, 3));

        using (var scope = Owner(w))
        {
            var s = await scope.ServiceProvider.GetRequiredService<GetClassSettingsHandler>().HandleAsync(default);
            Assert.Equal(new ClassSettingsResponse(4, 2, 15, 10, 14, 3, 60), s);

            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            Assert.Equal(1, await db.Set<ClassSettings>().CountAsync());
        }
    }

    [Fact]
    public async Task Out_of_range_settings_are_422()
    {
        var w = await SeedAsync(1);

        using var scope = Owner(w);
        await Assert.ThrowsAsync<ValidationDomainException>(() => scope.ServiceProvider
            .GetRequiredService<UpdateClassSettingsHandler>()
            .HandleAsync(new UpdateClassSettingsRequest(2, 0, 30, 7, 7, 5), default));
    }

    // ---------- Ventana de reserva ----------

    [Fact]
    public async Task Booking_beyond_the_booking_window_is_422_and_inside_it_works()
    {
        var w = await SeedAsync(1);
        var classId = await CreateClassAsync(w, [ClassDayOfWeek.Monday]);

        // Hoy es lunes; la ventana por defecto es de 7 días: el lunes siguiente entra, el de la semana después no.
        using (var scope = AsMember(w, 0, Monday, 12))
        {
            var book = scope.ServiceProvider.GetRequiredService<BookClassHandler>();

            var ok = await book.HandleAsync(classId, NextMonday, default);
            Assert.Equal(ClassBookingStatus.Booked.ToString(), ok.Status);

            var ex = await Assert.ThrowsAsync<ValidationDomainException>(
                () => book.HandleAsync(classId, NextMonday.AddDays(7), default));
            Assert.Contains("abren 7 días antes", ex.Message);
        }

        // Ampliar la ventana habilita la fecha.
        await UpdateSettingsAsync(w, new UpdateClassSettingsRequest(2, 3, 30, 7, 21, 5));

        using (var scope = AsMember(w, 0, Monday, 12))
        {
            var ok = await scope.ServiceProvider.GetRequiredService<BookClassHandler>()
                .HandleAsync(classId, NextMonday.AddDays(7), default);
            Assert.Equal(ClassBookingStatus.Booked.ToString(), ok.Status);
        }
    }

    // ---------- Máximo de reservas activas ----------

    [Fact]
    public async Task The_max_of_active_bookings_is_409_and_cancelling_frees_a_slot()
    {
        var w = await SeedAsync(1);
        var classId = await CreateClassAsync(
            w, [ClassDayOfWeek.Monday, ClassDayOfWeek.Tuesday, ClassDayOfWeek.Wednesday]);
        await UpdateSettingsAsync(w, new UpdateClassSettingsRequest(2, 3, 30, 7, 14, 2));

        using (var scope = AsMember(w, 0, Monday, 12))
        {
            var book = scope.ServiceProvider.GetRequiredService<BookClassHandler>();
            await book.HandleAsync(classId, NextMonday, default);
            await book.HandleAsync(classId, NextTuesday, default);

            var ex = await Assert.ThrowsAsync<ConflictException>(
                () => book.HandleAsync(classId, NextWednesday, default));
            Assert.Contains("máximo es 2", ex.Message);

            var status = await scope.ServiceProvider.GetRequiredService<GetMyBookingStatusHandler>().HandleAsync(default);
            Assert.Equal(2, status.ActiveBookings);
            Assert.Equal(2, status.MaxActiveBookings);
        }

        // Cancelar a tiempo libera un lugar (sigue lejos del plazo).
        using (var scope = AsMember(w, 0, Monday, 12))
        {
            await scope.ServiceProvider.GetRequiredService<CancelBookingHandler>()
                .HandleAsync(classId, NextMonday, default);

            var ok = await scope.ServiceProvider.GetRequiredService<BookClassHandler>()
                .HandleAsync(classId, NextWednesday, default);
            Assert.Equal(ClassBookingStatus.Booked.ToString(), ok.Status);
        }
    }

    // ---------- Plazo de cancelación ----------

    [Fact]
    public async Task Cancelling_before_the_deadline_does_not_count_as_a_no_show()
    {
        var w = await SeedAsync(1);
        var classId = await CreateClassAsync(w, [ClassDayOfWeek.Monday]); // 7:00

        await BookAsync(w, 0, classId, NextMonday);

        // El plazo es de 2 h: a las 4:00 de ese lunes todavía está a tiempo.
        using (var scope = AsMember(w, 0, NextMonday, 4))
        {
            var r = await scope.ServiceProvider.GetRequiredService<CancelBookingHandler>()
                .HandleAsync(classId, NextMonday, default);
            Assert.False(r.CountedAsNoShow);
            Assert.Null(r.BlockedUntil);
        }

        using (var scope = Owner(w))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var booking = await db.Set<ClassBooking>().SingleAsync();
            Assert.Equal(ClassBookingStatus.Cancelled, booking.Status);
            Assert.Null(booking.StrikeAt);
        }
    }

    [Fact]
    public async Task Cancelling_after_the_deadline_works_frees_the_slot_and_counts_as_a_no_show()
    {
        var w = await SeedAsync(1);
        var classId = await CreateClassAsync(w, [ClassDayOfWeek.Monday], capacity: 1);

        await BookAsync(w, 0, classId, NextMonday);

        // A las 6:00 faltan 60 min para la clase: fuera del plazo de 2 h.
        using (var scope = AsMember(w, 0, NextMonday, 6))
        {
            var r = await scope.ServiceProvider.GetRequiredService<CancelBookingHandler>()
                .HandleAsync(classId, NextMonday, default);
            Assert.True(r.CountedAsNoShow);
            Assert.Null(r.BlockedUntil); // el límite por defecto es 3
        }

        using (var scope = Owner(w))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var booking = await db.Set<ClassBooking>().SingleAsync();
            Assert.Equal(ClassBookingStatus.Cancelled, booking.Status);
            Assert.NotNull(booking.StrikeAt);

            var session = await db.Set<ClassSession>().SingleAsync();
            Assert.Equal(0, await db.Set<ClassBooking>().CountAsync(b => b.SessionId == session.Id && b.Status == ClassBookingStatus.Booked));
        }
    }

    // ---------- Penalización por no-show ----------

    [Fact]
    public async Task Reaching_the_limit_with_late_cancellations_blocks_new_bookings_but_keeps_existing_ones()
    {
        var w = await SeedAsync(2);
        var classId = await CreateClassAsync(
            w, [ClassDayOfWeek.Monday, ClassDayOfWeek.Tuesday, ClassDayOfWeek.Wednesday]);
        await UpdateSettingsAsync(w, new UpdateClassSettingsRequest(2, 2, 30, 7, 14, 5));

        await BookAsync(w, 0, classId, NextMonday);
        await BookAsync(w, 0, classId, NextTuesday);
        await BookAsync(w, 0, classId, NextWednesday); // se mantendrá

        using (var scope = AsMember(w, 0, NextMonday, 6))
        {
            var r = await scope.ServiceProvider.GetRequiredService<CancelBookingHandler>()
                .HandleAsync(classId, NextMonday, default);
            Assert.True(r.CountedAsNoShow);
            Assert.Null(r.BlockedUntil);
        }

        DateOnly blockedUntil;
        using (var scope = AsMember(w, 0, NextTuesday, 6))
        {
            var r = await scope.ServiceProvider.GetRequiredService<CancelBookingHandler>()
                .HandleAsync(classId, NextTuesday, default);
            Assert.True(r.CountedAsNoShow);

            // 7 días contando el día en que se alcanzó el límite.
            blockedUntil = Assert.NotNull(r.BlockedUntil);
            Assert.Equal(NextTuesday.AddDays(6), blockedUntil);
        }

        // Bloqueado: no puede reservar y el mensaje dice hasta cuándo.
        using (var scope = AsMember(w, 0, NextTuesday, 7))
        {
            var ex = await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<BookClassHandler>().HandleAsync(classId, NextWednesday.AddDays(7), default));
            Assert.Contains("No puedes reservar hasta el", ex.Message);
            Assert.Contains("por inasistencias", ex.Message);

            var status = await scope.ServiceProvider.GetRequiredService<GetMyBookingStatusHandler>().HandleAsync(default);
            Assert.Equal(blockedUntil, status.BlockedUntil);
        }

        // La reserva ya hecha se mantiene.
        using (var scope = Owner(w))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            Assert.Equal(1, await db.Set<ClassBooking>().CountAsync(b => b.Status == ClassBookingStatus.Booked));
        }

        // Otro miembro no se ve afectado.
        var otherClass = await CreateClassAsync(w, [ClassDayOfWeek.Friday]);
        using (var scope = AsMember(w, 1, NextTuesday, 7))
        {
            var ok = await scope.ServiceProvider.GetRequiredService<BookClassHandler>()
                .HandleAsync(otherClass, NextTuesday.AddDays(3), default);
            Assert.Equal(ClassBookingStatus.Booked.ToString(), ok.Status);
        }
    }

    [Fact]
    public async Task Staff_marking_no_show_counts_but_an_unmarked_booking_does_not()
    {
        var w = await SeedAsync(2);
        var classId = await CreateClassAsync(w, [ClassDayOfWeek.Monday, ClassDayOfWeek.Tuesday]);
        await UpdateSettingsAsync(w, new UpdateClassSettingsRequest(2, 2, 30, 7, 14, 5));

        await BookAsync(w, 0, classId, NextMonday);
        await BookAsync(w, 0, classId, NextTuesday);
        await BookAsync(w, 1, classId, NextMonday);
        await BookAsync(w, 1, classId, NextTuesday);

        // Miembro 0: el staff lo marca ausente las dos veces. Miembro 1: nadie toma asistencia.
        await MarkNoShowAsync(w, classId, NextMonday, w.MemberIds[0]);
        using (var scope = Owner(w))
        {
            var blocks = await scope.ServiceProvider.GetRequiredService<ListBookingBlocksHandler>().HandleAsync(default);
            Assert.Empty(blocks);
        }

        await MarkNoShowAsync(w, classId, NextTuesday, w.MemberIds[0]);

        using (var scope = Owner(w))
        {
            var blocks = await scope.ServiceProvider.GetRequiredService<ListBookingBlocksHandler>().HandleAsync(default);
            var block = Assert.Single(blocks);
            Assert.Equal(w.MemberIds[0], block.MemberId);
            Assert.Equal(2, block.StrikeCount);

            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var untouched = await db.Set<ClassBooking>().Where(b => b.MemberId == w.MemberIds[1]).ToListAsync();
            Assert.All(untouched, b =>
            {
                Assert.Equal(ClassBookingStatus.Booked, b.Status);
                Assert.Null(b.StrikeAt);
            });
        }
    }

    [Fact]
    public async Task Strikes_older_than_the_window_do_not_count()
    {
        var w = await SeedAsync(1);
        var classId = await CreateClassAsync(w, [ClassDayOfWeek.Monday]);
        await UpdateSettingsAsync(w, new UpdateClassSettingsRequest(2, 2, 30, 7, 60, 5));

        var first = NextMonday;
        var second = NextMonday.AddDays(35); // 35 días después: fuera de la ventana de 30
        await BookAsync(w, 0, classId, first);
        await BookAsync(w, 0, classId, second);

        using (var scope = AsMember(w, 0, first, 6))
        {
            var r = await scope.ServiceProvider.GetRequiredService<CancelBookingHandler>().HandleAsync(classId, first, default);
            Assert.True(r.CountedAsNoShow);
        }

        using (var scope = AsMember(w, 0, second, 6))
        {
            var r = await scope.ServiceProvider.GetRequiredService<CancelBookingHandler>().HandleAsync(classId, second, default);
            Assert.True(r.CountedAsNoShow);
            Assert.Null(r.BlockedUntil);
        }
    }

    [Fact]
    public async Task Staff_lifts_the_penalty_with_a_reason_and_the_count_starts_over()
    {
        var w = await SeedAsync(1);
        var classId = await CreateClassAsync(w, [ClassDayOfWeek.Monday, ClassDayOfWeek.Tuesday, ClassDayOfWeek.Wednesday]);
        await UpdateSettingsAsync(w, new UpdateClassSettingsRequest(2, 2, 30, 7, 14, 5));

        await BookAsync(w, 0, classId, NextMonday);
        await BookAsync(w, 0, classId, NextTuesday);
        await BookAsync(w, 0, classId, NextWednesday);

        await MarkNoShowAsync(w, classId, NextMonday, w.MemberIds[0]);
        await MarkNoShowAsync(w, classId, NextTuesday, w.MemberIds[0]);

        Guid blockId;
        using (var scope = Owner(w))
        {
            blockId = (await scope.ServiceProvider.GetRequiredService<ListBookingBlocksHandler>().HandleAsync(default)).Single().Id;

            var handler = scope.ServiceProvider.GetRequiredService<LiftBookingBlockHandler>();
            await Assert.ThrowsAsync<ValidationDomainException>(
                () => handler.HandleAsync(blockId, new LiftBookingBlockRequest("  "), default));

            await handler.HandleAsync(blockId, new LiftBookingBlockRequest("Estuvo hospitalizado"), default);

            await Assert.ThrowsAsync<ConflictException>(
                () => handler.HandleAsync(blockId, new LiftBookingBlockRequest("otra vez"), default));
        }

        using (var scope = Owner(w))
        {
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<ListBookingBlocksHandler>().HandleAsync(default));
        }

        // Puede reservar de nuevo...
        var friday = await CreateClassAsync(w, [ClassDayOfWeek.Friday]);
        using (var scope = AsMember(w, 0, Monday, 12))
        {
            var ok = await scope.ServiceProvider.GetRequiredService<BookClassHandler>()
                .HandleAsync(friday, Monday.AddDays(4), default);
            Assert.Equal(ClassBookingStatus.Booked.ToString(), ok.Status);
        }

        // ...y las inasistencias anteriores no vuelven a bloquearlo: hace falta llegar de nuevo al límite.
        await MarkNoShowAsync(w, classId, NextWednesday, w.MemberIds[0]);
        using (var scope = Owner(w))
        {
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<ListBookingBlocksHandler>().HandleAsync(default));
        }
    }

    // ---------- Siembra y ayudas ----------

    private async Task<Guid> CreateClassAsync(World w, ClassDayOfWeek[] days, int? capacity = 10)
    {
        using var scope = Owner(w);
        var created = await scope.ServiceProvider.GetRequiredService<CreateClassHandler>().HandleAsync(
            new SaveClassRequest("Spinning", null, "Ana", capacity, new TimeOnly(7, 0), 45,
                IsRecurring: true, DaysOfWeek: days, ScheduledDate: null), default);
        return created.Id;
    }

    private async Task UpdateSettingsAsync(World w, UpdateClassSettingsRequest request)
    {
        using var scope = Owner(w);
        await scope.ServiceProvider.GetRequiredService<UpdateClassSettingsHandler>().HandleAsync(request, default);
    }

    private async Task BookAsync(World w, int memberIndex, Guid classId, DateOnly date)
    {
        using var scope = AsMember(w, memberIndex, Monday, 12);
        await scope.ServiceProvider.GetRequiredService<BookClassHandler>().HandleAsync(classId, date, default);
    }

    private async Task MarkNoShowAsync(World w, Guid classId, DateOnly date, Guid memberId)
    {
        using var scope = Owner(w);
        await scope.ServiceProvider.GetRequiredService<MarkAttendanceHandler>()
            .HandleAsync(classId, date, new MarkAttendanceRequest(null, [memberId]), default);
    }

    private IServiceScope Owner(World w)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.StaffId, w.GymId, UserRole.Owner);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Monday);
        return scope;
    }

    /// <summary>Un miembro actuando a las <paramref name="hour"/> (hora del gym) del día <paramref name="day"/>.</summary>
    private IServiceScope AsMember(World w, int index, DateOnly day, int hour)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.MemberUserIds[index], w.GymId, UserRole.Member);
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
        clock.PinTo(clock.BusinessDayRange(day).Start.AddHours(hour), day);
        return scope;
    }

    private async Task<World> SeedAsync(int members)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Monday);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, attendance.attendances, achievements.achievements, achievements.member_achievements restart identity cascade;");
        await db.Database.ExecuteSqlRawAsync("delete from classes.gym_classes;");
        await db.Database.ExecuteSqlRawAsync("delete from classes.class_settings;");
        await db.Database.ExecuteSqlRawAsync("delete from classes.booking_blocks;");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("CR", $"cr-{suffix}");
        var staff = MakeUser($"cr-{suffix}-staff");
        gym.AssignOwner(staff.Id);
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);

        var plan = MembershipPlan.Create(gym.Id, "Mensual", null, 50000m, 30);
        db.Add(plan);

        var memberIds = new List<Guid>();
        var memberUserIds = new List<Guid>();
        for (var i = 0; i < members; i++)
        {
            var u = MakeUser($"cr-{suffix}-m{i}");
            var m = Member.Create(gym.Id, "Miembro", $"{i}", $"doc-{suffix}-{i}", new DateOnly(2026, 1, 1));
            m.LinkUser(u.Id);
            db.Add(u);
            db.Add(m);
            db.Add(Membership.Create(gym.Id, m.Id, plan.Id, Monday.AddDays(-5), 120));
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
