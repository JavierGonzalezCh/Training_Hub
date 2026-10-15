using GSM.Application.Classes;
using GSM.Application.Classes.Contracts;
using GSM.Application.Identity;
using GSM.Application.Identity.Contracts;
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
/// C2: el instructor sale de la lista de entrenadores del gym o es "Otro" (texto libre); "Mis clases" del
/// entrenador y asistencia marcada por él. Base de carril: gsm_test_clases.
/// </summary>
public sealed class ClassInstructorTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";

    // 2026-03-02 es lunes.
    private static readonly DateOnly Monday = new(2026, 3, 2);

    private static SaveClassRequest Weekly(string name, string? instructorName, Guid? instructorUserId)
        => new(name, null, instructorName, 15, new TimeOnly(7, 0), 45,
            IsRecurring: true, DaysOfWeek: [ClassDayOfWeek.Monday, ClassDayOfWeek.Wednesday],
            ScheduledDate: null, EndsOn: null, InstructorUserId: instructorUserId);

    [Fact]
    public async Task A_trainer_from_the_list_is_the_instructor_and_the_name_is_copied_from_the_account()
    {
        var w = await SeedAsync();
        await MakeTrainerAsync(w, w.CoachId);

        using var scope = As(w, w.OwnerId, UserRole.Owner);
        // El texto que mande el cliente se ignora: manda el nombre de la cuenta.
        var created = await scope.ServiceProvider.GetRequiredService<CreateClassHandler>()
            .HandleAsync(Weekly("Spinning", "Otro nombre", w.CoachId), default);

        Assert.Equal(w.CoachId, created.InstructorUserId);
        Assert.Equal("Coach Trainer", created.InstructorName);
        Assert.False(created.NoActiveInstructor);

        var options = await scope.ServiceProvider.GetRequiredService<ListClassInstructorsHandler>().HandleAsync(default);
        Assert.Contains(options, o => o.UserId == w.CoachId);
        Assert.Contains(options, o => o.UserId == w.OwnerId && o.IsOwner);
        Assert.DoesNotContain(options, o => o.UserId == w.ReceptionId);
    }

    [Fact]
    public async Task Other_keeps_the_free_text_and_has_no_account()
    {
        var w = await SeedAsync();

        using var scope = As(w, w.OwnerId, UserRole.Owner);
        var created = await scope.ServiceProvider.GetRequiredService<CreateClassHandler>()
            .HandleAsync(Weekly("Yoga", "Invitada Externa", null), default);

        Assert.Null(created.InstructorUserId);
        Assert.Equal("Invitada Externa", created.InstructorName);
        Assert.False(created.NoActiveInstructor);
    }

    [Fact]
    public async Task Someone_who_is_not_a_trainer_or_belongs_to_another_gym_is_a_422()
    {
        var w = await SeedAsync();

        using var scope = As(w, w.OwnerId, UserRole.Owner);
        var create = scope.ServiceProvider.GetRequiredService<CreateClassHandler>();

        await Assert.ThrowsAsync<ValidationDomainException>(() =>
            create.HandleAsync(Weekly("Mala", null, w.ReceptionId), default));
        await Assert.ThrowsAsync<ValidationDomainException>(() =>
            create.HandleAsync(Weekly("Mala", null, w.OtherOwnerId), default));
    }

    [Fact]
    public async Task My_classes_lists_only_the_ones_where_I_am_the_instructor()
    {
        var w = await SeedAsync();
        await MakeTrainerAsync(w, w.CoachId);

        using (var scope = As(w, w.OwnerId, UserRole.Owner))
        {
            var create = scope.ServiceProvider.GetRequiredService<CreateClassHandler>();
            await create.HandleAsync(Weekly("Spinning", null, w.CoachId), default);
            await create.HandleAsync(Weekly("Boxeo", null, w.OwnerId), default);
            await create.HandleAsync(Weekly("Yoga", "Invitada Externa", null), default);
        }

        using (var scope = As(w, w.CoachId, UserRole.Staff))
        {
            var mine = await scope.ServiceProvider.GetRequiredService<GetMyInstructorClassesHandler>()
                .HandleAsync(Monday, Monday.AddDays(6), default);

            Assert.Equal(2, mine.Entries.Count); // lunes y miércoles
            Assert.All(mine.Entries, e =>
            {
                Assert.Equal("Spinning", e.ClassName);
                Assert.Equal(w.CoachId, e.InstructorUserId);
            });
        }

        using (var scope = As(w, w.OwnerId, UserRole.Owner))
        {
            var mine = await scope.ServiceProvider.GetRequiredService<GetMyInstructorClassesHandler>()
                .HandleAsync(Monday, Monday.AddDays(6), default);

            Assert.All(mine.Entries, e => Assert.Equal("Boxeo", e.ClassName));
            Assert.Equal(2, mine.Entries.Count);
        }
    }

    [Fact]
    public async Task The_trainer_marks_attendance_of_their_own_class()
    {
        var w = await SeedAsync();
        await MakeTrainerAsync(w, w.CoachId);

        Guid classId;
        using (var scope = As(w, w.OwnerId, UserRole.Owner))
        {
            classId = (await scope.ServiceProvider.GetRequiredService<CreateClassHandler>()
                .HandleAsync(Weekly("Spinning", null, w.CoachId), default)).Id;
        }

        using (var scope = As(w, w.MemberUserId, UserRole.Member))
        {
            await scope.ServiceProvider.GetRequiredService<BookClassHandler>().HandleAsync(classId, Monday, default);
        }

        using (var scope = As(w, w.CoachId, UserRole.Staff))
        {
            var roster = await scope.ServiceProvider.GetRequiredService<MarkAttendanceHandler>()
                .HandleAsync(classId, Monday, new MarkAttendanceRequest([w.MemberId], null), default);

            Assert.Equal(ClassBookingStatus.Attended.ToString(), roster.Entries.Single().Status);

            // La sesión materializada conserva al entrenador y sigue saliendo en "Mis clases".
            var mine = await scope.ServiceProvider.GetRequiredService<GetMyInstructorClassesHandler>()
                .HandleAsync(Monday, Monday, default);
            var entry = Assert.Single(mine.Entries);
            Assert.True(entry.IsMaterialized);
            Assert.Equal(w.CoachId, entry.InstructorUserId);
        }
    }

    [Fact]
    public async Task Reassigning_the_instructor_moves_future_sessions_and_keeps_past_ones()
    {
        var w = await SeedAsync();
        await MakeTrainerAsync(w, w.CoachId);

        Guid classId;
        using (var scope = As(w, w.OwnerId, UserRole.Owner))
        {
            classId = (await scope.ServiceProvider.GetRequiredService<CreateClassHandler>()
                .HandleAsync(Weekly("Spinning", null, w.CoachId), default)).Id;
        }

        // Una sesión de hoy (lunes) y otra futura (miércoles) materializadas por sendas reservas.
        using (var scope = As(w, w.MemberUserId, UserRole.Member))
        {
            var book = scope.ServiceProvider.GetRequiredService<BookClassHandler>();
            await book.HandleAsync(classId, Monday, default);
            await book.HandleAsync(classId, Monday.AddDays(2), default);
        }

        // Pasa el tiempo: hoy es el martes, la sesión del lunes ya fue.
        using (var scope = As(w, w.OwnerId, UserRole.Owner))
        {
            scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Monday.AddDays(1));
            await scope.ServiceProvider.GetRequiredService<UpdateClassHandler>()
                .HandleAsync(classId, Weekly("Spinning", null, w.OwnerId), default);
        }

        using (var scope = As(w, w.OwnerId, UserRole.Owner))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var sessions = await db.Set<ClassSession>().Where(s => s.ClassId == classId).ToListAsync();
            Assert.Equal(w.CoachId, sessions.Single(s => s.SessionDate == Monday).InstructorUserId);
            Assert.Equal(w.OwnerId, sessions.Single(s => s.SessionDate == Monday.AddDays(2)).InstructorUserId);
        }
    }

    [Fact]
    public async Task A_trainer_who_loses_the_permission_leaves_the_class_flagged_for_reassignment()
    {
        var w = await SeedAsync();
        await MakeTrainerAsync(w, w.CoachId);

        Guid classId;
        using (var scope = As(w, w.OwnerId, UserRole.Owner))
        {
            classId = (await scope.ServiceProvider.GetRequiredService<CreateClassHandler>()
                .HandleAsync(Weekly("Spinning", null, w.CoachId), default)).Id;
            await scope.ServiceProvider.GetRequiredService<UpdateStaffPermissionsHandler>()
                .HandleAsync(w.CoachId, new UpdateStaffPermissionsRequest("reception", []), default);
        }

        using (var scope = As(w, w.OwnerId, UserRole.Owner))
        {
            var list = await scope.ServiceProvider.GetRequiredService<ListClassesHandler>().HandleAsync(false, default);
            var item = list.Single(c => c.Id == classId);
            Assert.True(item.NoActiveInstructor);
            Assert.Equal("Coach Trainer", item.InstructorName); // conserva el nombre

            var detail = await scope.ServiceProvider.GetRequiredService<GetClassHandler>().HandleAsync(classId, default);
            Assert.True(detail.NoActiveInstructor);

            // Editar la clase sin tocar al instructor no exige que siga siendo entrenador...
            var kept = await scope.ServiceProvider.GetRequiredService<UpdateClassHandler>()
                .HandleAsync(classId, Weekly("Spinning 2", null, w.CoachId), default);
            Assert.Equal(w.CoachId, kept.InstructorUserId);

            // ...pero reasignarla a alguien que no lo es, sí.
            await Assert.ThrowsAsync<ValidationDomainException>(() =>
                scope.ServiceProvider.GetRequiredService<UpdateClassHandler>()
                    .HandleAsync(classId, Weekly("Spinning 2", null, w.ReceptionId), default));

            // Reasignarla a "Otro" la saca de la lista de pendientes.
            var other = await scope.ServiceProvider.GetRequiredService<UpdateClassHandler>()
                .HandleAsync(classId, Weekly("Spinning 2", "Invitado", null), default);
            Assert.Null(other.InstructorUserId);
            Assert.False(other.NoActiveInstructor);
        }
    }

    [Fact]
    public async Task Classes_created_before_C2_stay_as_Other_with_their_text()
    {
        var w = await SeedAsync();
        using var scope = As(w, w.OwnerId, UserRole.Owner);
        var created = await scope.ServiceProvider.GetRequiredService<CreateClassHandler>()
            .HandleAsync(Weekly("Vieja", "Ana", null), default);

        var list = await scope.ServiceProvider.GetRequiredService<ListClassesHandler>().HandleAsync(false, default);
        var item = list.Single(c => c.Id == created.Id);
        Assert.Equal("Ana", item.InstructorName);
        Assert.Null(item.InstructorUserId);
        Assert.False(item.NoActiveInstructor);
    }

    private IServiceScope As(World w, Guid userId, UserRole role)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(userId, w.GymId, role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Monday);
        return scope;
    }

    private async Task MakeTrainerAsync(World w, Guid staffId)
    {
        using var scope = As(w, w.OwnerId, UserRole.Owner);
        await scope.ServiceProvider.GetRequiredService<UpdateStaffPermissionsHandler>()
            .HandleAsync(staffId, new UpdateStaffPermissionsRequest("trainer", ["trainer", "view_member_health"]), default);
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Monday);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var provisioning = scope.ServiceProvider.GetRequiredService<GSM.Application.Identity.Public.IUserProvisioning>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members restart identity cascade;");
        await db.Database.ExecuteSqlRawAsync("delete from classes.gym_classes;");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("CI", $"ci-{suffix}");
        var other = Gym.Create("CI B", $"cib-{suffix}");
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), other, GymFeatureFlags.Default(other.Id));
        await db.SaveChangesAsync();

        var ownerId = await provisioning.CreateGymUserAsync(
            $"owner-{suffix}@test.local", "Trainer Owner", Password, UserRole.Owner, gym.Id);
        var coachId = await provisioning.CreateGymUserAsync(
            $"coach-{suffix}@test.local", "Coach Trainer", Password, UserRole.Staff, gym.Id);
        var receptionId = await provisioning.CreateGymUserAsync(
            $"rec-{suffix}@test.local", "Rita Reception", Password, UserRole.Staff, gym.Id);
        gym.AssignOwner(ownerId);
        var otherOwnerId = await provisioning.CreateGymUserAsync(
            $"ownerb-{suffix}@test.local", "Other Owner", Password, UserRole.Owner, other.Id);
        other.AssignOwner(otherOwnerId);

        var memberUserId = await provisioning.CreateMemberUserAsync(
            $"member-{suffix}@test.local", "Mia Member", Password, gym.Id);
        var member = Member.Create(gym.Id, "Mia", "Member", $"9{suffix}", new DateOnly(2026, 1, 1));
        member.LinkUser(memberUserId);
        db.Add(member);

        var plan = MembershipPlan.Create(gym.Id, "Mensual", null, 50000m, 30);
        db.Add(plan);
        db.Add(Membership.Create(gym.Id, member.Id, plan.Id, Monday.AddDays(-5), 90));
        await db.SaveChangesAsync();

        return new World(gym.Id, ownerId, coachId, receptionId, otherOwnerId, member.Id, memberUserId);
    }

    private sealed record World(
        Guid GymId, Guid OwnerId, Guid CoachId, Guid ReceptionId, Guid OtherOwnerId, Guid MemberId, Guid MemberUserId);
}
