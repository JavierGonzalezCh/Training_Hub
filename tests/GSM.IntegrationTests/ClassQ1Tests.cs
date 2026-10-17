using GSM.Application.Classes;
using GSM.Application.Classes.Contracts;
using GSM.Application.Memberships;
using GSM.Application.Memberships.Contracts;
using GSM.Application.Memberships.Public;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Domain.Notifications;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// Q1 de Classes: el cupo nuevo llega a las sesiones ya materializadas y a la lista de espera (C4-1), la lista
/// de espera también recibe el aviso de clase cancelada (C4-2), una oferta de quien ya no está al día pasa al
/// siguiente de inmediato (C4-3), acortar el fin de la serie cancela lo posterior (C5-3) y la tiquetera con
/// clases funciona de punta a punta (C1-1). Cada prueba siembra su propio gym.
/// </summary>
public sealed class ClassQ1Tests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Monday = new(2026, 3, 2);
    private static readonly DateOnly NextMonday = Monday.AddDays(7);

    // ---------- C4-1: subir o bajar el cupo ----------

    [Fact]
    public async Task Raising_the_capacity_updates_the_materialized_session_and_offers_the_new_spot_to_the_waitlist()
    {
        var w = await SeedAsync(3);
        var classId = await CreateClassAsync(w, capacity: 1);
        await BookAsync(w, 0, classId);
        await JoinAsync(w, 1, classId);
        await JoinAsync(w, 2, classId);

        await UpdateClassAsync(w, classId, capacity: 2);

        var session = await SessionAsync(classId, NextMonday);
        Assert.Equal(2, session.Capacity);

        // Un lugar nuevo: se le ofrece al primero de la lista; el segundo sigue esperando.
        var entries = await EntriesAsync(w.GymId);
        Assert.Equal(WaitlistEntryStatus.Offered, entries.Single(e => e.MemberId == w.MemberIds[1]).Status);
        Assert.Equal(WaitlistEntryStatus.Waiting, entries.Single(e => e.MemberId == w.MemberIds[2]).Status);
        Assert.Single(await NoticesAsync(w.GymId, NotificationType.WaitlistOffer));
    }

    [Fact]
    public async Task Lowering_the_capacity_updates_the_session_but_never_cancels_existing_bookings()
    {
        var w = await SeedAsync(2);
        var classId = await CreateClassAsync(w, capacity: 2);
        await BookAsync(w, 0, classId);
        await BookAsync(w, 1, classId);

        await UpdateClassAsync(w, classId, capacity: 1);

        Assert.Equal(1, (await SessionAsync(classId, NextMonday)).Capacity);
        Assert.Equal(2, await BookedCountAsync(w, classId));
    }

    // ---------- C4-2: la lista de espera también recibe el aviso de clase cancelada ----------

    [Fact]
    public async Task Cancelling_a_session_notifies_the_waitlist_too_and_closes_it()
    {
        var w = await SeedAsync(3);
        var classId = await CreateClassAsync(w, capacity: 1);
        await BookAsync(w, 0, classId);
        await JoinAsync(w, 1, classId);
        await JoinAsync(w, 2, classId);

        using (var scope = Owner(w))
        {
            await scope.ServiceProvider.GetRequiredService<CancelSessionHandler>()
                .HandleAsync(classId, NextMonday, "Festivo", default);
        }

        var notices = await NoticesAsync(w.GymId, NotificationType.ClassCancelled);
        Assert.Equal(3, notices.Count);
        Assert.Equal(
            w.MemberIds.OrderBy(i => i), notices.Select(n => n.RecipientMemberId!.Value).OrderBy(i => i));
        Assert.Contains(notices, n => n.RecipientMemberId == w.MemberIds[1] && n.Body.Contains("lista de espera"));
        Assert.Contains(notices, n => n.RecipientMemberId == w.MemberIds[0] && n.Body.Contains("reserva"));

        Assert.All(await EntriesAsync(w.GymId), e => Assert.Equal(WaitlistEntryStatus.Expired, e.Status));
    }

    [Fact]
    public async Task Deactivating_a_class_notifies_the_waitlist_of_its_sessions()
    {
        var w = await SeedAsync(2);
        var classId = await CreateClassAsync(w, capacity: 1);
        await BookAsync(w, 0, classId);
        await JoinAsync(w, 1, classId);

        using (var scope = Owner(w))
        {
            await scope.ServiceProvider.GetRequiredService<DeactivateClassHandler>().HandleAsync(classId, default);
        }

        var notices = await NoticesAsync(w.GymId, NotificationType.ClassCancelled);
        Assert.Equal(2, notices.Count);
        Assert.Contains(notices, n => n.RecipientMemberId == w.MemberIds[1]);
        Assert.Equal(WaitlistEntryStatus.Expired, (await EntriesAsync(w.GymId)).Single().Status);
    }

    // ---------- C4-3: confirmar sin estar al día libera el cupo de inmediato ----------

    [Fact]
    public async Task Confirming_an_offer_when_no_longer_up_to_date_expires_it_and_passes_the_spot_to_the_next()
    {
        var w = await SeedAsync(3);
        var classId = await CreateClassAsync(w, capacity: 1);
        await BookAsync(w, 0, classId);
        await JoinAsync(w, 1, classId);
        await JoinAsync(w, 2, classId);

        var cancelAt = AtLocal(Monday, 12);
        await CancelAsync(w, 0, classId, cancelAt);
        Assert.Equal(
            WaitlistEntryStatus.Offered, (await EntriesAsync(w.GymId)).Single(e => e.MemberId == w.MemberIds[1]).Status);

        // La membresía del primero se cancela mientras tenía la oferta.
        await CancelMembershipAsync(w, 1);

        var confirmAt = AtLocal(Monday, 12, 20);
        using (var scope = AsMember(w, 1, confirmAt))
        {
            var ex = await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<ConfirmWaitlistOfferHandler>().HandleAsync(classId, NextMonday, default));
            Assert.DoesNotContain("venció", ex.Message);
        }

        // La oferta no se queda apartada: venció ya y el siguiente tiene su propio plazo.
        var entries = await EntriesAsync(w.GymId);
        Assert.Equal(WaitlistEntryStatus.Expired, entries.Single(e => e.MemberId == w.MemberIds[1]).Status);
        var next = entries.Single(e => e.MemberId == w.MemberIds[2]);
        Assert.Equal(WaitlistEntryStatus.Offered, next.Status);
        Assert.Equal(confirmAt.AddMinutes(60), next.OfferExpiresAt);
        Assert.Equal(0, await BookedCountAsync(w, classId));
    }

    // ---------- C5-3: acortar el fin de la serie ----------

    [Fact]
    public async Task Shortening_the_series_end_cancels_later_sessions_with_bookings_and_notifies()
    {
        var w = await SeedAsync(1);
        var classId = await CreateClassAsync(w, capacity: 10);
        await UpdateSettingsAsync(w, new UpdateClassSettingsRequest(2, 3, 30, 7, 21, 5));
        var later = NextMonday.AddDays(7);
        await BookAsync(w, 0, classId, NextMonday);
        await BookAsync(w, 0, classId, later);

        // La serie termina el martes siguiente al primer lunes: el segundo lunes queda fuera.
        await UpdateClassAsync(w, classId, capacity: 10, endsOn: NextMonday.AddDays(1));

        var kept = await SessionAsync(classId, NextMonday);
        Assert.False(kept.IsCancelled);
        var dropped = await SessionAsync(classId, later);
        Assert.True(dropped.IsCancelled);
        Assert.Equal("La clase terminó antes", dropped.CancellationReason);

        var statuses = await DbAsync(db => (
            from b in db.Set<ClassBooking>()
            join s in db.Set<ClassSession>() on b.SessionId equals s.Id
            where b.GymId == w.GymId
            select new { s.SessionDate, b.Status }).ToListAsync());
        Assert.Equal(ClassBookingStatus.Booked, statuses.Single(x => x.SessionDate == NextMonday).Status);
        Assert.Equal(ClassBookingStatus.Cancelled, statuses.Single(x => x.SessionDate == later).Status);

        Assert.Single(await NoticesAsync(w.GymId, NotificationType.ClassCancelled));
    }

    // ---------- C1-1: clase con tiquetera ----------

    [Fact]
    public async Task A_pass_member_can_book_and_attending_the_class_uses_one_entry()
    {
        var w = await SeedAsync(1, withMembership: false);
        await AssignPassAsync(w, entries: 2);
        var classId = await CreateClassAsync(w, capacity: 10);

        // Reservar exige estar al día: la tiquetera con entradas cuenta.
        await BookAsync(w, 0, classId);
        Assert.Equal(2, await EntriesLeftAsync(w, AtLocal(Monday, 13)));

        // El staff marca la asistencia: ClassAttendanceRegistered registra el check-in del día y descuenta la entrada.
        using (var scope = AsOwner(w, AtLocal(NextMonday, 8)))
        {
            await scope.ServiceProvider.GetRequiredService<MarkAttendanceHandler>()
                .HandleAsync(classId, NextMonday, new MarkAttendanceRequest([w.MemberIds[0]], null), default);
        }

        Assert.Equal(1, await EntriesLeftAsync(w, AtLocal(NextMonday, 9)));
    }

    // ---------- Siembra y ayudas ----------

    private async Task<Guid> CreateClassAsync(World w, int? capacity = 1)
    {
        using var scope = Owner(w);
        var created = await scope.ServiceProvider.GetRequiredService<CreateClassHandler>().HandleAsync(
            new SaveClassRequest("Spinning", null, "Ana", capacity, new TimeOnly(7, 0), 45,
                IsRecurring: true, DaysOfWeek: [ClassDayOfWeek.Monday], ScheduledDate: null), default);
        return created.Id;
    }

    private async Task UpdateClassAsync(World w, Guid classId, int? capacity, DateOnly? endsOn = null)
    {
        using var scope = Owner(w);
        await scope.ServiceProvider.GetRequiredService<UpdateClassHandler>().HandleAsync(
            classId,
            new SaveClassRequest("Spinning", null, "Ana", capacity, new TimeOnly(7, 0), 45,
                IsRecurring: true, DaysOfWeek: [ClassDayOfWeek.Monday], ScheduledDate: null, EndsOn: endsOn),
            default);
    }

    private async Task UpdateSettingsAsync(World w, UpdateClassSettingsRequest request)
    {
        using var scope = Owner(w);
        await scope.ServiceProvider.GetRequiredService<UpdateClassSettingsHandler>().HandleAsync(request, default);
    }

    private async Task BookAsync(World w, int memberIndex, Guid classId, DateOnly? date = null)
    {
        using var scope = AsMember(w, memberIndex, AtLocal(Monday, 12));
        await scope.ServiceProvider.GetRequiredService<BookClassHandler>().HandleAsync(classId, date ?? NextMonday, default);
    }

    private async Task JoinAsync(World w, int memberIndex, Guid classId)
    {
        using var scope = AsMember(w, memberIndex, AtLocal(Monday, 12));
        await scope.ServiceProvider.GetRequiredService<JoinWaitlistHandler>().HandleAsync(classId, NextMonday, default);
    }

    private async Task CancelAsync(World w, int memberIndex, Guid classId, DateTimeOffset at)
    {
        using var scope = AsMember(w, memberIndex, at);
        await scope.ServiceProvider.GetRequiredService<CancelBookingHandler>().HandleAsync(classId, NextMonday, default);
    }

    private async Task CancelMembershipAsync(World w, int memberIndex)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var memberId = w.MemberIds[memberIndex];
        var membership = await db.Set<Membership>().SingleAsync(m => m.MemberId == memberId);
        membership.Cancel("Prueba", AtLocal(Monday, 12, 10));
        await db.SaveChangesAsync();
    }

    private async Task AssignPassAsync(World w, int entries)
    {
        using var scope = AsOwner(w, AtLocal(Monday, 9));
        var planId = (await scope.ServiceProvider.GetRequiredService<CreatePlanHandler>().HandleAsync(
            new CreatePlanRequest(
                $"Tiquetera {Guid.NewGuid():N}"[..20], null, 100000m, 30, null, Kind: "pass", Entries: entries,
                ValidityDays: 30), default)).Id;
        await scope.ServiceProvider.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(w.MemberIds[0], new AssignMembershipRequest(planId, null), default);
    }

    private async Task<int?> EntriesLeftAsync(World w, DateTimeOffset at)
    {
        using var scope = AsOwner(w, at);
        return (await scope.ServiceProvider.GetRequiredService<IMembershipReader>()
            .GetAccessStatusAsync(w.MemberIds[0])).EntriesLeft;
    }

    private Task<ClassSession> SessionAsync(Guid classId, DateOnly date)
        => DbAsync(db => db.Set<ClassSession>().AsNoTracking().SingleAsync(s => s.ClassId == classId && s.SessionDate == date));

    private Task<List<WaitlistEntry>> EntriesAsync(Guid gymId)
        => DbAsync(db => db.Set<WaitlistEntry>().Where(e => e.GymId == gymId).OrderBy(e => e.Position).ToListAsync());

    private Task<List<Notification>> NoticesAsync(Guid gymId, NotificationType type)
        => DbAsync(db => db.Set<Notification>().Where(n => n.GymId == gymId && n.Type == type).ToListAsync());

    private Task<int> BookedCountAsync(World w, Guid classId)
        => DbAsync(db => (
            from b in db.Set<ClassBooking>()
            join s in db.Set<ClassSession>() on b.SessionId equals s.Id
            where b.GymId == w.GymId && s.ClassId == classId && b.Status == ClassBookingStatus.Booked
            select b.Id).CountAsync());

    private async Task<T> DbAsync<T>(Func<GsmDbContext, Task<T>> query)
    {
        using var scope = System();
        return await query(scope.ServiceProvider.GetRequiredService<GsmDbContext>());
    }

    private static DateTimeOffset AtLocal(DateOnly day, int hour, int minute = 0)
        => new GSM.Infrastructure.Common.SystemClock(
                Microsoft.Extensions.Options.Options.Create(new GSM.Infrastructure.Common.ClockOptions { TimeZone = "America/Bogota" }))
            .BusinessDayRange(day).Start.AddHours(hour).AddMinutes(minute);

    private static DateOnly DayOf(DateTimeOffset instant)
        => DateOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(-5)).DateTime);

    private IServiceScope System()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }

    private IServiceScope Owner(World w) => AsOwner(w, AtLocal(Monday, 9));

    private IServiceScope AsOwner(World w, DateTimeOffset at)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.StaffId, w.GymId, UserRole.Owner);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(at, DayOf(at));
        return scope;
    }

    private IServiceScope AsMember(World w, int index, DateTimeOffset at)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.MemberUserIds[index], w.GymId, UserRole.Member);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(at, DayOf(at));
        return scope;
    }

    private async Task<World> SeedAsync(int members, bool withMembership = true)
    {
        using var scope = System();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Monday);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("Q1", $"q1-{suffix}");
        var staff = MakeUser($"q1-{suffix}-staff");
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
            var u = MakeUser($"q1-{suffix}-m{i}");
            var m = Member.Create(gym.Id, "Miembro", $"{i}", $"doc-{suffix}-{i}", new DateOnly(2026, 1, 1));
            m.LinkUser(u.Id);
            db.Add(u);
            db.Add(m);
            if (withMembership)
            {
                db.Add(Membership.Create(gym.Id, m.Id, plan.Id, Monday.AddDays(-5), 120));
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

    private sealed record World(Guid GymId, Guid StaffId, List<Guid> MemberIds, List<Guid> MemberUserIds);
}
