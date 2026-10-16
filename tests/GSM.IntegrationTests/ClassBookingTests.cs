using GSM.Application.Classes;
using GSM.Application.Classes.Contracts;
using GSM.Domain.Achievements;
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

public sealed class ClassBookingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Monday = new(2026, 3, 2);
    private static readonly DateOnly NextMonday = Monday.AddDays(7);

    [Fact]
    public async Task Capacity_is_enforced_and_freed_when_a_booking_is_cancelled()
    {
        var world = await SeedAsync(members: 12);
        var classId = await CreateClassAsync(world, capacity: 10);

        // Las primeras 10 reservan sin problema.
        for (var i = 0; i < 10; i++)
        {
            using var scope = AsMember(world, world.MemberUserIds[i]);
            var r = await scope.ServiceProvider.GetRequiredService<BookClassHandler>()
                .HandleAsync(classId, NextMonday, default);
            Assert.Equal(ClassBookingStatus.Booked.ToString(), r.Status);
        }

        // La 11.ª recibe 409.
        using (var scope = AsMember(world, world.MemberUserIds[10]))
        {
            await Assert.ThrowsAsync<ConflictException>(() =>
                scope.ServiceProvider.GetRequiredService<BookClassHandler>()
                    .HandleAsync(classId, NextMonday, default));
        }

        // Una cancela → vuelve a haber sitio.
        using (var scope = AsMember(world, world.MemberUserIds[0]))
        {
            await scope.ServiceProvider.GetRequiredService<CancelBookingHandler>()
                .HandleAsync(classId, NextMonday, default);
        }

        using (var scope = AsMember(world, world.MemberUserIds[10]))
        {
            var r = await scope.ServiceProvider.GetRequiredService<BookClassHandler>()
                .HandleAsync(classId, NextMonday, default);
            Assert.Equal(ClassBookingStatus.Booked.ToString(), r.Status);
        }

        // Solo una sesión materializada pese a las 12 reservas.
        using (var scope = Staff(world))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            Assert.Equal(1, await db.Set<ClassSession>().CountAsync(s => s.ClassId == classId));
        }
    }

    [Fact]
    public async Task Deactivating_a_class_cancels_its_future_bookings_and_notifies_the_members()
    {
        var world = await SeedAsync(members: 2);
        var classId = await CreateClassAsync(world, capacity: 10);

        foreach (var userId in world.MemberUserIds)
        {
            using var scope = AsMember(world, userId);
            await scope.ServiceProvider.GetRequiredService<BookClassHandler>().HandleAsync(classId, NextMonday, default);
        }

        using (var scope = Staff(world))
        {
            await scope.ServiceProvider.GetRequiredService<DeactivateClassHandler>().HandleAsync(classId, default);
        }

        // N5-6: la sesión futura queda cancelada con su motivo, las reservas también y cada miembro recibe el aviso.
        using (var scope = Staff(world))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var session = await db.Set<ClassSession>().SingleAsync(s => s.ClassId == classId && s.SessionDate == NextMonday);
            Assert.True(session.IsCancelled);
            Assert.Equal(DeactivateClassHandler.CancellationReason, session.CancellationReason);
            Assert.All(
                await db.Set<ClassBooking>().Where(b => b.SessionId == session.Id).ToListAsync(),
                b => Assert.False(b.IsActive));

            var notified = await db.Set<GSM.Domain.Notifications.Notification>()
                .Where(n => n.GymId == world.GymId && n.Type == GSM.Domain.Notifications.NotificationType.ClassCancelled)
                .Select(n => n.RecipientMemberId)
                .ToListAsync();
            Assert.Equal(world.MemberIds.OrderBy(x => x), notified.Select(x => x!.Value).OrderBy(x => x));
        }

        // Dar de baja otra vez no repite nada.
        using (var scope = Staff(world))
        {
            await scope.ServiceProvider.GetRequiredService<DeactivateClassHandler>().HandleAsync(classId, default);
        }
    }

    [Fact]
    public async Task Class_without_capacity_rejects_booking_but_attendance_still_works()
    {
        var world = await SeedAsync(members: 2);
        var classId = await CreateClassAsync(world, capacity: null);

        using (var scope = AsMember(world, world.MemberUserIds[0]))
        {
            await Assert.ThrowsAsync<ValidationDomainException>(() =>
                scope.ServiceProvider.GetRequiredService<BookClassHandler>()
                    .HandleAsync(classId, Monday, default));
        }

        using (var scope = Staff(world))
        {
            var roster = await scope.ServiceProvider.GetRequiredService<MarkAttendanceHandler>()
                .HandleAsync(classId, Monday,
                    new MarkAttendanceRequest([world.MemberIds[0], world.MemberIds[1]], null), default);

            Assert.Equal(2, roster.Entries.Count);
            Assert.All(roster.Entries, e => Assert.Equal(ClassBookingStatus.Attended.ToString(), e.Status));
        }
    }

    [Fact]
    public async Task Marking_attendance_registers_the_daily_check_in_without_duplicating()
    {
        var world = await SeedAsync(members: 2);
        var classId = await CreateClassAsync(world, capacity: 10);

        // El miembro 0 ya fichó al entrar hoy.
        using (var scope = Staff(world))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            db.Add(AttendanceEntity.Create(world.GymId, world.MemberIds[0], AttendanceMethod.Qr,
                new DateTimeOffset(2026, 3, 2, 12, 0, 0, TimeSpan.Zero), Monday));
            await db.SaveChangesAsync();
        }

        using (var scope = Staff(world))
        {
            await scope.ServiceProvider.GetRequiredService<MarkAttendanceHandler>()
                .HandleAsync(classId, Monday,
                    new MarkAttendanceRequest([world.MemberIds[0], world.MemberIds[1]], null), default);
        }

        using (var scope = Staff(world))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            Assert.Equal(1, await db.Set<AttendanceEntity>()
                .CountAsync(a => a.MemberId == world.MemberIds[0] && a.AttendanceDate == Monday));
            Assert.Equal(1, await db.Set<AttendanceEntity>()
                .CountAsync(a => a.MemberId == world.MemberIds[1] && a.AttendanceDate == Monday));
        }
    }

    [Fact]
    public async Task Cancelling_the_session_cancels_its_bookings()
    {
        var world = await SeedAsync(members: 2);
        var classId = await CreateClassAsync(world, capacity: 10);

        using (var scope = AsMember(world, world.MemberUserIds[0]))
        {
            await scope.ServiceProvider.GetRequiredService<BookClassHandler>()
                .HandleAsync(classId, NextMonday, default);
        }

        using (var scope = Staff(world))
        {
            await scope.ServiceProvider.GetRequiredService<CancelSessionHandler>()
                .HandleAsync(classId, NextMonday, "Instructor enfermo", default);
        }

        using (var scope = AsMember(world, world.MemberUserIds[0]))
        {
            var history = await scope.ServiceProvider.GetRequiredService<GetMyClassHistoryHandler>()
                .HandleAsync(default);
            Assert.Equal(ClassBookingStatus.Cancelled.ToString(), history.Single().Status);
        }
    }

    [Fact]
    public async Task Member_booking_is_403_when_show_classes_is_off_but_staff_still_operates()
    {
        var world = await SeedAsync(members: 1);
        var classId = await CreateClassAsync(world, capacity: 10);

        await SetShowClassesAsync(world.GymId, false);

        using (var scope = AsMember(world, world.MemberUserIds[0]))
        {
            await Assert.ThrowsAsync<ForbiddenDomainException>(() =>
                scope.ServiceProvider.GetRequiredService<BookClassHandler>()
                    .HandleAsync(classId, NextMonday, default));
        }

        using (var scope = Staff(world))
        {
            var roster = await scope.ServiceProvider.GetRequiredService<GetRosterHandler>()
                .HandleAsync(classId, NextMonday, default);
            Assert.Empty(roster.Entries);
        }
    }

    [Fact]
    public async Task Attending_a_class_unlocks_a_classes_achievement()
    {
        var world = await SeedAsync(members: 1);
        var classId = await CreateClassAsync(world, capacity: 10);

        using (var scope = Staff(world))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            db.Add(Achievement.Create(
                world.GymId, "Primera clase", null, null, AchievementConditionType.Classes, 1));
            await db.SaveChangesAsync();
        }

        using (var scope = Staff(world))
        {
            await scope.ServiceProvider.GetRequiredService<MarkAttendanceHandler>()
                .HandleAsync(classId, Monday, new MarkAttendanceRequest([world.MemberIds[0]], null), default);
        }

        using (var scope = Staff(world))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            Assert.Equal(1, await db.Set<MemberAchievement>()
                .CountAsync(m => m.MemberId == world.MemberIds[0]));
        }
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

    private IServiceScope AsMember(World world, Guid userId)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(userId, world.GymId, UserRole.Member);
        return scope;
    }

    private async Task SetShowClassesAsync(Guid gymId, bool value)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "update gyms.gym_feature_flags set show_classes = {0} where gym_id = {1};", value, gymId);
        scope.Dispose();
    }

    private async Task<World> SeedAsync(int members)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Monday);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, attendance.attendances, achievements.achievements, achievements.member_achievements restart identity cascade;");
        await db.Database.ExecuteSqlRawAsync("delete from classes.gym_classes;");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("CB", $"cb-{suffix}");
        var staff = MakeUser($"cb-{suffix}-staff", UserRole.Staff, gym.Id);
        gym.AssignOwner(staff.Id);
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);

        // C1: reservar y asistir exige estar al día; todos los miembros de estos tests tienen plan vigente.
        var plan = MembershipPlan.Create(gym.Id, "Mensual", null, 50000m, 30);
        db.Add(plan);

        var memberIds = new List<Guid>();
        var memberUserIds = new List<Guid>();
        for (var i = 0; i < members; i++)
        {
            var u = MakeUser($"cb-{suffix}-m{i}", UserRole.Member, gym.Id);
            var m = Member.Create(gym.Id, "Miembro", $"{i}", $"doc-{suffix}-{i}", new DateOnly(2026, 1, 1));
            m.LinkUser(u.Id);
            db.Add(u);
            db.Add(m);
            db.Add(Membership.Create(gym.Id, m.Id, plan.Id, Monday.AddDays(-5), 90));
            memberIds.Add(m.Id);
            memberUserIds.Add(u.Id);
        }

        await db.SaveChangesAsync();
        scope.Dispose();
        return new World(gym.Id, staff.Id, memberIds, memberUserIds);
    }

    private static GSM.Infrastructure.Identity.AppUser MakeUser(string local, UserRole role, Guid gymId)
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
