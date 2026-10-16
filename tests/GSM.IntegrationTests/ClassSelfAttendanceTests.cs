using GSM.Application.Classes;
using GSM.Application.Classes.Contracts;
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

/// <summary>C7: el miembro marca su propia asistencia ("Estoy en clase").</summary>
public sealed class ClassSelfAttendanceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Monday = new(2026, 3, 2);

    // La clase es a las 07:00 hora del gym (UTC-5): 12:00 UTC.
    private static readonly DateTimeOffset ClassStartUtc = new(2026, 3, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_booked_member_who_checked_in_can_mark_attendance_inside_the_window()
    {
        var world = await SeedAsync();
        var classId = await CreateClassAsync(world, capacity: 10);
        await BookAsync(world, classId);
        await CheckInAsync(world);

        PinClock(ClassStartUtc.AddMinutes(-10));
        using (var scope = AsMember(world))
        {
            var r = await scope.ServiceProvider.GetRequiredService<MarkOwnAttendanceHandler>()
                .HandleAsync(classId, Monday, default);
            Assert.Equal(ClassBookingStatus.Attended.ToString(), r.Status);
            Assert.NotNull(r.AttendedAt);
        }

        // No se registró otra entrada: sigue habiendo una sola del día.
        using (var scope = Staff(world))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            Assert.Equal(1, await db.Set<AttendanceEntity>()
                .CountAsync(a => a.MemberId == world.MemberId && a.AttendanceDate == Monday));
        }
    }

    [Fact]
    public async Task Marking_twice_is_idempotent()
    {
        var world = await SeedAsync();
        var classId = await CreateClassAsync(world, capacity: 10);
        await BookAsync(world, classId);
        await CheckInAsync(world);

        PinClock(ClassStartUtc);
        SelfAttendanceResponse first;
        using (var scope = AsMember(world))
        {
            first = await scope.ServiceProvider.GetRequiredService<MarkOwnAttendanceHandler>()
                .HandleAsync(classId, Monday, default);
        }

        using (var scope = AsMember(world))
        {
            var second = await scope.ServiceProvider.GetRequiredService<MarkOwnAttendanceHandler>()
                .HandleAsync(classId, Monday, default);
            Assert.Equal(first.BookingId, second.BookingId);
            Assert.Equal(first.AttendedAt, second.AttendedAt);
        }
    }

    [Fact]
    public async Task Outside_the_15_minute_window_is_rejected_with_422()
    {
        var world = await SeedAsync();
        var classId = await CreateClassAsync(world, capacity: 10);
        await BookAsync(world, classId);
        await CheckInAsync(world);

        foreach (var offset in new[] { -30, 30 })
        {
            PinClock(ClassStartUtc.AddMinutes(offset));
            using var scope = AsMember(world);
            await Assert.ThrowsAsync<ValidationDomainException>(() =>
                scope.ServiceProvider.GetRequiredService<MarkOwnAttendanceHandler>()
                    .HandleAsync(classId, Monday, default));
        }
    }

    [Fact]
    public async Task Without_todays_gym_check_in_it_is_409()
    {
        var world = await SeedAsync();
        var classId = await CreateClassAsync(world, capacity: 10);
        await BookAsync(world, classId);

        PinClock(ClassStartUtc);
        using var scope = AsMember(world);
        await Assert.ThrowsAsync<ConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<MarkOwnAttendanceHandler>()
                .HandleAsync(classId, Monday, default));
    }

    [Fact]
    public async Task A_class_with_capacity_requires_an_active_booking()
    {
        var world = await SeedAsync();
        var classId = await CreateClassAsync(world, capacity: 10);
        await CheckInAsync(world);

        PinClock(ClassStartUtc);
        using var scope = AsMember(world);
        await Assert.ThrowsAsync<ConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<MarkOwnAttendanceHandler>()
                .HandleAsync(classId, Monday, default));
    }

    [Fact]
    public async Task A_free_entry_class_needs_no_booking()
    {
        var world = await SeedAsync();
        var classId = await CreateClassAsync(world, capacity: null);
        await CheckInAsync(world);

        PinClock(ClassStartUtc.AddMinutes(5));
        using var scope = AsMember(world);
        var r = await scope.ServiceProvider.GetRequiredService<MarkOwnAttendanceHandler>()
            .HandleAsync(classId, Monday, default);
        Assert.Equal(ClassBookingStatus.Attended.ToString(), r.Status);
    }

    [Fact]
    public async Task A_member_who_is_not_up_to_date_cannot_mark_attendance()
    {
        var world = await SeedAsync(membershipDays: 1); // vencida desde antes del lunes y fuera de la gracia
        var classId = await CreateClassAsync(world, capacity: null);
        await CheckInAsync(world);

        PinClock(ClassStartUtc);
        using var scope = AsMember(world);
        await Assert.ThrowsAsync<ConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<MarkOwnAttendanceHandler>()
                .HandleAsync(classId, Monday, default));
    }

    [Fact]
    public async Task A_cancelled_session_cannot_be_marked()
    {
        var world = await SeedAsync();
        var classId = await CreateClassAsync(world, capacity: 10);
        await BookAsync(world, classId);
        await CheckInAsync(world);

        using (var scope = Staff(world))
        {
            await scope.ServiceProvider.GetRequiredService<CancelSessionHandler>()
                .HandleAsync(classId, Monday, "Instructor enfermo", default);
        }

        PinClock(ClassStartUtc);
        using (var scope = AsMember(world))
        {
            await Assert.ThrowsAsync<ConflictException>(() =>
                scope.ServiceProvider.GetRequiredService<MarkOwnAttendanceHandler>()
                    .HandleAsync(classId, Monday, default));
        }
    }

    private void PinClock(DateTimeOffset utcNow)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(utcNow, Monday);
    }

    private async Task BookAsync(World world, Guid classId)
    {
        PinClock(ClassStartUtc.AddHours(-1));
        using var scope = AsMember(world);
        await scope.ServiceProvider.GetRequiredService<BookClassHandler>().HandleAsync(classId, Monday, default);
    }

    private async Task CheckInAsync(World world)
    {
        using var scope = Staff(world);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        db.Add(AttendanceEntity.Create(world.GymId, world.MemberId, AttendanceMethod.Qr, ClassStartUtc.AddMinutes(-30), Monday));
        await db.SaveChangesAsync();
    }

    private async Task<Guid> CreateClassAsync(World world, int? capacity)
    {
        using var scope = Staff(world);
        var created = await scope.ServiceProvider.GetRequiredService<CreateClassHandler>()
            .HandleAsync(new SaveClassRequest(
                "Spinning", null, "Ana", capacity, new TimeOnly(7, 0), 45,
                IsRecurring: true, DaysOfWeek: [ClassDayOfWeek.Monday], ScheduledDate: null), default);
        return created.Id;
    }

    private IServiceScope Staff(World world)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(world.StaffId, world.GymId, UserRole.Staff);
        return scope;
    }

    private IServiceScope AsMember(World world)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(world.MemberUserId, world.GymId, UserRole.Member);
        return scope;
    }

    private async Task<World> SeedAsync(int membershipDays = 90)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Monday);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, attendance.attendances, achievements.achievements, achievements.member_achievements restart identity cascade;");
        await db.Database.ExecuteSqlRawAsync("delete from classes.gym_classes;");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("CS", $"cs-{suffix}");
        var staff = MakeUser($"cs-{suffix}-staff");
        gym.AssignOwner(staff.Id);
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);

        var plan = MembershipPlan.Create(gym.Id, "Mensual", null, 50000m, 30);
        db.Add(plan);

        var user = MakeUser($"cs-{suffix}-m");
        var member = Member.Create(gym.Id, "Miembro", "Uno", $"doc-{suffix}", new DateOnly(2026, 1, 1));
        member.LinkUser(user.Id);
        db.Add(user);
        db.Add(member);
        // Con membershipDays corto vence mucho antes del lunes (fuera de los 3 días de gracia por defecto).
        db.Add(Membership.Create(gym.Id, member.Id, plan.Id, Monday.AddDays(-5 - 30), membershipDays == 90 ? 90 + 30 : membershipDays));

        await db.SaveChangesAsync();
        scope.Dispose();
        return new World(gym.Id, staff.Id, member.Id, user.Id);
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

    private sealed record World(Guid GymId, Guid StaffId, Guid MemberId, Guid MemberUserId);
}
