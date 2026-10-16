using GSM.Application.Classes;
using GSM.Application.Classes.Contracts;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Equipment;
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
/// Classes v2, C6: cupo ligado a máquinas. El cupo efectivo de una sesión es el menor entre el cupo de la clase y las
/// unidades de las máquinas ligadas disponibles ese día; se calcula al vuelo (no se copia a la sesión). Una máquina que
/// sale de servicio con la sesión llena no cancela a nadie; las nuevas reservas sí respetan el cupo efectivo, y la lista
/// de espera ofrece los cupos que el cupo efectivo libera. Cada prueba siembra su propio gym.
/// </summary>
public sealed class ClassMachineCapacityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Monday = new(2026, 3, 2);
    private static readonly DateOnly NextMonday = Monday.AddDays(7);

    // ---------- Ligar máquinas a la clase ----------

    [Fact]
    public async Task A_class_can_be_created_and_edited_with_machines_and_the_response_lists_them()
    {
        var w = await SeedAsync(1);
        var bikeA = await AddMachineAsync(w, "Bici 1");
        var bikeB = await AddMachineAsync(w, "Bici 2");

        var classId = await CreateClassAsync(w, capacity: 10, machines: [bikeA, bikeB, bikeA]);

        using (var scope = Owner(w))
        {
            var response = await scope.ServiceProvider.GetRequiredService<GetClassHandler>().HandleAsync(classId, default);
            Assert.Equal(new[] { bikeA, bikeB }.Order(), response.EquipmentItemIds.Order());
        }

        // Sin lista en la edición: las ligas no se tocan.
        var kept = await UpdateClassAsync(w, classId, capacity: 10, machines: null);
        Assert.Equal(2, kept.EquipmentItemIds.Count);

        // Con lista: quedan exactamente esas.
        var replaced = await UpdateClassAsync(w, classId, capacity: 10, machines: [bikeB]);
        Assert.Equal(new[] { bikeB }, replaced.EquipmentItemIds);

        // Lista vacía: las quita todas.
        var cleared = await UpdateClassAsync(w, classId, capacity: 10, machines: []);
        Assert.Empty(cleared.EquipmentItemIds);
        Assert.Empty(await LinksAsync(classId));
    }

    [Fact]
    public async Task Linking_an_unknown_inactive_or_foreign_machine_is_422()
    {
        var w = await SeedAsync(1);
        var other = await SeedAsync(1);
        var foreign = await AddMachineAsync(other, "Bici ajena");
        var retired = await AddMachineAsync(w, "Bici vieja");
        await DeactivateMachineAsync(retired);

        await Assert.ThrowsAsync<ValidationDomainException>(() => CreateClassAsync(w, 10, [Guid.NewGuid()]));
        await Assert.ThrowsAsync<ValidationDomainException>(() => CreateClassAsync(w, 10, [foreign]));
        await Assert.ThrowsAsync<ValidationDomainException>(() => CreateClassAsync(w, 10, [retired]));
    }

    [Fact]
    public async Task A_free_entry_class_cannot_be_linked_to_machines_nor_lose_its_capacity_while_linked()
    {
        var w = await SeedAsync(1);
        var bike = await AddMachineAsync(w, "Bici 1");

        await Assert.ThrowsAsync<ValidationDomainException>(() => CreateClassAsync(w, capacity: null, machines: [bike]));

        var classId = await CreateClassAsync(w, capacity: 5, machines: [bike]);
        // Pasarla a entrada libre sin quitar las ligas se rechaza: quitarlas es explícito.
        await Assert.ThrowsAsync<ValidationDomainException>(() => UpdateClassAsync(w, classId, capacity: null, machines: null));
        var freed = await UpdateClassAsync(w, classId, capacity: null, machines: []);
        Assert.Null(freed.Capacity);
    }

    // ---------- Cupo efectivo ----------

    [Fact]
    public async Task Bookings_respect_the_smaller_of_the_class_capacity_and_the_available_machines()
    {
        var w = await SeedAsync(4);
        var bikeA = await AddMachineAsync(w, "Bici 1");
        var bikeB = await AddMachineAsync(w, "Bici 2");
        var classId = await CreateClassAsync(w, capacity: 10, machines: [bikeA, bikeB]);

        await BookAsync(w, 0, classId);
        await BookAsync(w, 1, classId);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => BookAsync(w, 2, classId));
        Assert.Contains("2 máquinas disponibles", ex.Message);

        var roster = await RosterAsync(w, classId);
        Assert.Equal(10, roster.Capacity);
        Assert.Equal(2, roster.EffectiveCapacity);
        Assert.Equal(2, roster.AvailableMachines);
        Assert.Equal(2, roster.LinkedMachines);
    }

    [Fact]
    public async Task The_class_capacity_still_wins_when_there_are_more_machines_than_seats()
    {
        var w = await SeedAsync(3);
        var bikes = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            bikes.Add(await AddMachineAsync(w, $"Bici {i}"));
        }

        var classId = await CreateClassAsync(w, capacity: 2, machines: bikes);
        await BookAsync(w, 0, classId);
        await BookAsync(w, 1, classId);
        await Assert.ThrowsAsync<ConflictException>(() => BookAsync(w, 2, classId));

        var roster = await RosterAsync(w, classId);
        Assert.Equal(2, roster.EffectiveCapacity);
        Assert.Equal(4, roster.AvailableMachines);
    }

    [Fact]
    public async Task A_machine_item_counts_as_many_units_as_its_quantity()
    {
        var w = await SeedAsync(4);
        var rack = await AddMachineAsync(w, "Bicicletas de la zona", quantity: 3);
        var classId = await CreateClassAsync(w, capacity: 10, machines: [rack]);

        await BookAsync(w, 0, classId);
        await BookAsync(w, 1, classId);
        await BookAsync(w, 2, classId);
        await Assert.ThrowsAsync<ConflictException>(() => BookAsync(w, 3, classId));

        Assert.Equal(3, (await RosterAsync(w, classId)).EffectiveCapacity);
    }

    [Fact]
    public async Task Machines_in_maintenance_do_not_count_unless_they_are_back_by_the_session_day()
    {
        var w = await SeedAsync(1);
        var ok = await AddMachineAsync(w, "Bici sana");
        var backSoon = await AddMachineAsync(w, "Bici en arreglo corto");
        var backLate = await AddMachineAsync(w, "Bici en arreglo largo");
        var noDate = await AddMachineAsync(w, "Bici sin fecha");
        await SetStatusAsync(backSoon, EquipmentStatus.Maintenance, expectedBackOn: NextMonday);
        await SetStatusAsync(backLate, EquipmentStatus.Maintenance, expectedBackOn: NextMonday.AddDays(1));
        await SetStatusAsync(noDate, EquipmentStatus.OutOfService, expectedBackOn: null);
        var classId = await CreateClassAsync(w, capacity: 10, machines: [ok, backSoon, backLate, noDate]);

        var roster = await RosterAsync(w, classId);

        // La sana y la que vuelve el mismo día; no la que vuelve después ni la que no tiene fecha.
        Assert.Equal(2, roster.AvailableMachines);
        Assert.Equal(2, roster.EffectiveCapacity);
        Assert.Equal(4, roster.LinkedMachines);
    }

    [Fact]
    public async Task A_class_without_machines_is_untouched_and_reports_no_machine_figures()
    {
        var w = await SeedAsync(2);
        var classId = await CreateClassAsync(w, capacity: 1);
        await BookAsync(w, 0, classId);
        await Assert.ThrowsAsync<ConflictException>(() => BookAsync(w, 1, classId));

        var roster = await RosterAsync(w, classId);
        Assert.Null(roster.EffectiveCapacity);
        Assert.Null(roster.AvailableMachines);
        Assert.Null(roster.LinkedMachines);
    }

    // ---------- Una máquina sale de servicio con la sesión llena ----------

    [Fact]
    public async Task A_machine_going_out_of_service_cancels_nobody_but_blocks_new_bookings_and_shows_in_the_roster()
    {
        var w = await SeedAsync(3);
        var bikeA = await AddMachineAsync(w, "Bici 1");
        var bikeB = await AddMachineAsync(w, "Bici 2");
        var classId = await CreateClassAsync(w, capacity: 10, machines: [bikeA, bikeB]);
        await BookAsync(w, 0, classId);
        await BookAsync(w, 1, classId);

        await SetStatusAsync(bikeB, EquipmentStatus.Maintenance, expectedBackOn: null);

        var roster = await RosterAsync(w, classId);
        Assert.Equal(2, roster.BookedCount);
        Assert.All(roster.Entries, e => Assert.Equal("Booked", e.Status));
        Assert.Equal(1, roster.AvailableMachines);
        Assert.Equal(1, roster.EffectiveCapacity);

        await Assert.ThrowsAsync<ConflictException>(() => BookAsync(w, 2, classId));
        Assert.Equal(2, await BookedCountAsync(w, classId));
    }

    [Fact]
    public async Task The_effective_capacity_is_not_copied_to_the_session_so_a_returning_machine_frees_seats()
    {
        var w = await SeedAsync(2);
        var bikeA = await AddMachineAsync(w, "Bici 1");
        var bikeB = await AddMachineAsync(w, "Bici 2");
        await SetStatusAsync(bikeB, EquipmentStatus.Maintenance, expectedBackOn: null);
        var classId = await CreateClassAsync(w, capacity: 10, machines: [bikeA, bikeB]);

        await BookAsync(w, 0, classId);
        await Assert.ThrowsAsync<ConflictException>(() => BookAsync(w, 1, classId));
        Assert.Equal(10, (await SessionAsync(classId)).Capacity);

        await SetStatusAsync(bikeB, EquipmentStatus.Available, expectedBackOn: null);
        await BookAsync(w, 1, classId);
        Assert.Equal(2, await BookedCountAsync(w, classId));
    }

    // ---------- Lista de espera ----------

    [Fact]
    public async Task A_machine_coming_back_makes_the_waitlist_offer_the_new_seat_to_the_first_in_line()
    {
        var w = await SeedAsync(2);
        var bikeA = await AddMachineAsync(w, "Bici 1");
        var bikeB = await AddMachineAsync(w, "Bici 2");
        await SetStatusAsync(bikeB, EquipmentStatus.Maintenance, expectedBackOn: null);
        var classId = await CreateClassAsync(w, capacity: 10, machines: [bikeA, bikeB]);
        await BookAsync(w, 0, classId);
        await JoinAsync(w, 1, classId);
        Assert.Equal(WaitlistEntryStatus.Waiting, (await EntriesAsync(w.GymId)).Single().Status);

        await SetStatusAsync(bikeB, EquipmentStatus.Available, expectedBackOn: null);
        await ProcessWaitlistAsync(w, classId, AtLocal(Monday, 12));

        var entry = (await EntriesAsync(w.GymId)).Single();
        Assert.Equal(WaitlistEntryStatus.Offered, entry.Status);
        Assert.Equal(w.MemberIds[1], entry.MemberId);
    }

    [Fact]
    public async Task A_cancelled_booking_is_not_offered_when_the_machines_no_longer_cover_it()
    {
        var w = await SeedAsync(3);
        var bikeA = await AddMachineAsync(w, "Bici 1");
        var bikeB = await AddMachineAsync(w, "Bici 2");
        var classId = await CreateClassAsync(w, capacity: 10, machines: [bikeA, bikeB]);
        await BookAsync(w, 0, classId);
        await BookAsync(w, 1, classId);
        await JoinAsync(w, 2, classId);

        // Una bici sale de servicio: 2 reservas, 1 máquina. Cancelar una deja 1 reserva y 1 máquina: sin cupo libre.
        await SetStatusAsync(bikeB, EquipmentStatus.Maintenance, expectedBackOn: null);
        await CancelAsync(w, 0, classId, AtLocal(Monday, 12));

        Assert.Equal(WaitlistEntryStatus.Waiting, (await EntriesAsync(w.GymId)).Single().Status);
    }

    [Fact]
    public async Task With_no_machines_available_an_untouched_occurrence_is_already_full_and_the_waitlist_takes_the_member()
    {
        var w = await SeedAsync(1);
        var bike = await AddMachineAsync(w, "Bici 1");
        await SetStatusAsync(bike, EquipmentStatus.OutOfService, expectedBackOn: null);
        var classId = await CreateClassAsync(w, capacity: 5, machines: [bike]);

        // Booking is refused; the schedule says "full" even though the session was never materialized.
        using (var scope = AsMember(w, 0, AtLocal(Monday, 12)))
        {
            var schedule = await scope.ServiceProvider.GetRequiredService<GetMyClassesHandler>()
                .HandleAsync(NextMonday, NextMonday, default);
            var entry = Assert.Single(schedule.Entries);
            Assert.True(entry.IsFull);
            Assert.Equal(0, entry.EffectiveCapacity);
            Assert.Equal(0, entry.AvailableMachines);
        }

        var joined = await JoinAsync(w, 0, classId);
        Assert.Equal("Waiting", joined.Status);
    }

    // ---------- Horario ----------

    [Fact]
    public async Task The_staff_schedule_shows_the_effective_capacity_only_for_linked_classes()
    {
        var w = await SeedAsync(1);
        var bike = await AddMachineAsync(w, "Bici 1");
        var linked = await CreateClassAsync(w, capacity: 10, machines: [bike]);
        var plain = await CreateClassAsync(w, capacity: 10);

        using var scope = Owner(w);
        var schedule = await scope.ServiceProvider.GetRequiredService<GetScheduleHandler>()
            .HandleAsync(NextMonday, NextMonday, default);

        var withMachines = schedule.Entries.Single(e => e.ClassId == linked);
        Assert.Equal(1, withMachines.EffectiveCapacity);
        Assert.Equal(1, withMachines.AvailableMachines);
        Assert.Equal(10, withMachines.Capacity);
        var withoutMachines = schedule.Entries.Single(e => e.ClassId == plain);
        Assert.Null(withoutMachines.EffectiveCapacity);
    }

    // ---------- Siembra y ayudas ----------

    private async Task<Guid> CreateClassAsync(World w, int? capacity, List<Guid>? machines = null)
    {
        using var scope = Owner(w);
        var created = await scope.ServiceProvider.GetRequiredService<CreateClassHandler>().HandleAsync(
            new SaveClassRequest("Spinning", null, "Ana", capacity, new TimeOnly(7, 0), 45,
                IsRecurring: true, DaysOfWeek: [ClassDayOfWeek.Monday], ScheduledDate: null, EquipmentItemIds: machines), default);
        return created.Id;
    }

    private async Task<ClassResponse> UpdateClassAsync(World w, Guid classId, int? capacity, List<Guid>? machines)
    {
        using var scope = Owner(w);
        return await scope.ServiceProvider.GetRequiredService<UpdateClassHandler>().HandleAsync(
            classId,
            new SaveClassRequest("Spinning", null, "Ana", capacity, new TimeOnly(7, 0), 45,
                IsRecurring: true, DaysOfWeek: [ClassDayOfWeek.Monday], ScheduledDate: null, EquipmentItemIds: machines),
            default);
    }

    private async Task<RosterResponse> RosterAsync(World w, Guid classId)
    {
        using var scope = Owner(w);
        return await scope.ServiceProvider.GetRequiredService<GetRosterHandler>().HandleAsync(classId, NextMonday, default);
    }

    private async Task BookAsync(World w, int memberIndex, Guid classId)
    {
        using var scope = AsMember(w, memberIndex, AtLocal(Monday, 12));
        await scope.ServiceProvider.GetRequiredService<BookClassHandler>().HandleAsync(classId, NextMonday, default);
    }

    private async Task<WaitlistResponse> JoinAsync(World w, int memberIndex, Guid classId)
    {
        using var scope = AsMember(w, memberIndex, AtLocal(Monday, 12));
        return await scope.ServiceProvider.GetRequiredService<JoinWaitlistHandler>().HandleAsync(classId, NextMonday, default);
    }

    private async Task CancelAsync(World w, int memberIndex, Guid classId, DateTimeOffset at)
    {
        using var scope = AsMember(w, memberIndex, at);
        await scope.ServiceProvider.GetRequiredService<CancelBookingHandler>().HandleAsync(classId, NextMonday, default);
    }

    private async Task ProcessWaitlistAsync(World w, Guid classId, DateTimeOffset at)
    {
        var session = await SessionAsync(classId);
        using var scope = AsOwner(w, at);
        await scope.ServiceProvider.GetRequiredService<WaitlistService>().ProcessAsync(session.Id, default);
    }

    private async Task<Guid> AddMachineAsync(World w, string name, int quantity = 1)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var item = EquipmentItem.Create(
            w.GymId, null, name, EquipmentType.Cardio_machine, "Spinning", null, null, null, quantity, null, null);
        db.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    private async Task SetStatusAsync(Guid itemId, EquipmentStatus status, DateOnly? expectedBackOn)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var item = await db.Set<EquipmentItem>().IgnoreQueryFilters().SingleAsync(i => i.Id == itemId);
        db.Add(item.ChangeStatus(status, status == EquipmentStatus.Available ? null : "Prueba", expectedBackOn, Monday, null, AtLocal(Monday, 9)));
        await db.SaveChangesAsync();
    }

    private async Task DeactivateMachineAsync(Guid itemId)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        (await db.Set<EquipmentItem>().IgnoreQueryFilters().SingleAsync(i => i.Id == itemId)).Deactivate();
        await db.SaveChangesAsync();
    }

    private Task<List<ClassEquipmentItem>> LinksAsync(Guid classId)
        => DbAsync(db => db.Set<ClassEquipmentItem>().IgnoreQueryFilters().Where(l => l.ClassId == classId).ToListAsync());

    private Task<ClassSession> SessionAsync(Guid classId)
        => DbAsync(db => db.Set<ClassSession>().IgnoreQueryFilters().SingleAsync(s => s.ClassId == classId && s.SessionDate == NextMonday));

    private Task<List<WaitlistEntry>> EntriesAsync(Guid gymId)
        => DbAsync(db => db.Set<WaitlistEntry>().Where(e => e.GymId == gymId).OrderBy(e => e.Position).ToListAsync());

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

    private async Task<World> SeedAsync(int members)
    {
        using var scope = System();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Monday);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("CM", $"cm-{suffix}");
        var staff = MakeUser($"cm-{suffix}-staff");
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
            var u = MakeUser($"cm-{suffix}-m{i}");
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

    private sealed record World(Guid GymId, Guid StaffId, List<Guid> MemberIds, List<Guid> MemberUserIds);
}
