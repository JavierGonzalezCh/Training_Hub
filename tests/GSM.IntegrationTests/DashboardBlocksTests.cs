using GSM.Application.Dashboard;
using GSM.Application.Equipment.Public;
using GSM.Application.Payments.Public;
using GSM.Domain.Attendance;
using GSM.Domain.Equipment;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Payments;
using GSM.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;
using AttMethod = GSM.Domain.Attendance.AttendanceMethod;

namespace GSM.IntegrationTests;

/// <summary>
/// D1: el panel del gym por bloques (Dashboards §1, §4 y §7). Un bloque que quien entra no puede ver llega nulo (no
/// 403), un lector que falla deja nulo solo su bloque, los días se cuentan en la zona del gym y los datos de otro gym
/// nunca aparecen. El comparativo de ingresos (B2) tiene su propio test en <c>DashboardRevenueComparisonTests</c>.
/// </summary>
public sealed class DashboardBlocksTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Day = new(2026, 6, 15);

    private sealed record World(Guid GymId, Guid OwnerId, Guid StaffId, Guid OtherStaffId, Guid MemberId);

    // ---------- criterio 1: sin el permiso, el bloque llega nulo ----------

    [Fact]
    public async Task A_user_without_the_permission_gets_the_block_as_null_not_a_403()
    {
        var w = await SeedAsync();
        await SeedPendingVoidRequestAsync(w);
        await SeedOpenCashAsync(w, w.OtherStaffId);

        var staff = await RunAsync(w, w.StaffId, UserRole.Staff);

        Assert.NotNull(staff.Indicators);
        Assert.NotNull(staff.Attention);
        Assert.NotNull(staff.Today);
        Assert.Null(staff.Finances);
        Assert.Null(staff.Attention!.VoidRequests);
        Assert.Null(staff.Attention.Cash); // la caja abierta es de otro y no tiene ViewFinances
        Assert.Null(staff.MyMembers);      // no es entrenador

        var owner = await RunAsync(w, w.OwnerId, UserRole.Owner);

        Assert.NotNull(owner.Finances);
        Assert.Equal(1, owner.Attention!.VoidRequests!.Pending);
        var cash = Assert.IsType<CashBlock>(owner.Attention.Cash);
        Assert.False(cash.IsMine);
        Assert.Equal(50_000m, cash.ExpectedCash);
    }

    [Fact]
    public async Task Each_permission_opens_only_its_own_block()
    {
        var w = await SeedAsync();
        await SeedPendingVoidRequestAsync(w);
        await SeedOpenCashAsync(w, w.OtherStaffId);

        var finances = await RunAsync(w, w.StaffId, UserRole.Staff, u => u.Permissions.Add(StaffPermission.ViewFinances));
        Assert.NotNull(finances.Finances);
        Assert.NotNull(finances.Attention!.Cash);
        Assert.Null(finances.Attention.VoidRequests);

        var voids = await RunAsync(w, w.StaffId, UserRole.Staff, u => u.Permissions.Add(StaffPermission.VoidPayments));
        Assert.Null(voids.Finances);
        Assert.Null(voids.Attention!.Cash);
        Assert.Equal(1, voids.Attention.VoidRequests!.Pending);
    }

    [Fact]
    public async Task Staff_without_ViewFinances_sees_their_own_open_register()
    {
        var w = await SeedAsync();
        await SeedOpenCashAsync(w, w.StaffId);

        var dash = await RunAsync(w, w.StaffId, UserRole.Staff);

        Assert.True(dash.Attention!.Cash!.IsMine);
    }

    [Fact]
    public async Task The_trainer_block_counts_their_own_members_without_a_routine()
    {
        var w = await SeedAsync();
        await AssignTrainerAsync(w, w.StaffId);

        var trainer = await RunAsync(w, w.StaffId, UserRole.Staff, u => u.Permissions.Add(StaffPermission.Trainer));
        Assert.Equal(1, trainer.MyMembers!.MembersWithoutRoutine);
        Assert.Equal(w.MemberId, Assert.Single(trainer.MyMembers.WithoutRoutine).MemberId);

        var plain = await RunAsync(w, w.StaffId, UserRole.Staff);
        Assert.Null(plain.MyMembers);
    }

    [Fact]
    public async Task Today_classes_is_an_empty_list_when_there_are_none()
    {
        var w = await SeedAsync();

        var dash = await RunAsync(w, w.StaffId, UserRole.Staff);

        Assert.NotNull(dash.Today!.Classes);
        Assert.Empty(dash.Today.Classes!);
    }

    [Fact]
    public async Task Equipment_lists_machines_out_of_service_with_the_unsafe_ones_first()
    {
        var w = await SeedAsync();
        await SeedOutOfServiceMachinesAsync(w);

        var dash = await RunAsync(w, w.StaffId, UserRole.Staff);

        var equipment = dash.Attention!.Equipment!;
        Assert.Equal(2, equipment.OutOfService);
        Assert.Equal(1, equipment.OpenIssues);
        Assert.Equal(1, equipment.UrgentIssues);
        Assert.Equal("Prensa", equipment.Machines.First().Name);
        Assert.True(equipment.Machines.First().HasUnsafeIssue);
        Assert.False(equipment.Machines.Last().HasUnsafeIssue);
    }

    // ---------- criterio 2: un lector que lanza deja su bloque en null ----------

    [Fact]
    public async Task A_failing_reader_leaves_its_block_null_and_the_rest_of_the_response_arrives()
    {
        var w = await SeedAsync();

        using var scope = factory.CreateScopeWithMigration();
        Pin(scope, w, w.OwnerId, UserRole.Owner, null);
        var handler = ActivatorUtilities.CreateInstance<GetDashboardHandler>(
            scope.ServiceProvider, new ThrowingPaymentReader(), new ThrowingEquipmentReader());

        var dash = await handler.HandleAsync(default);

        Assert.Null(dash.Finances);                     // IPaymentReader falló
        Assert.NotNull(dash.Attention);                 // el bloque de atención sigue, sin sus partes rotas
        Assert.Null(dash.Attention!.Equipment);         // IEquipmentReader falló
        Assert.Null(dash.Attention.Cash);               // IPaymentReader falló
        Assert.Null(dash.Attention.VoidRequests);       // IPaymentReader falló
        Assert.NotNull(dash.Attention.Rejected);        // Attendance responde
        Assert.NotNull(dash.Indicators);
        Assert.NotNull(dash.Today);
        Assert.NotNull(dash.PeakHours);
        Assert.NotNull(dash.Expiring);
    }

    // ---------- criterio 3: los días se cuentan en la zona del gym ----------

    [Fact]
    public async Task Counts_by_day_use_the_gym_zone_at_8pm_Colombia_time()
    {
        var w = await SeedAsync();
        var clock = factory.Services.GetRequiredService<MutableClock>();
        var eightPm = clock.BusinessDayRange(Day).Start.AddHours(20); // 01:00 UTC del día siguiente

        using (var seed = factory.CreateScopeWithMigration())
        {
            seed.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            var db = seed.ServiceProvider.GetRequiredService<GsmDbContext>();
            db.Add(AttendanceEntity.Create(w.GymId, w.MemberId, AttMethod.Manual, eightPm, Day));
            db.Add(CheckinAttempt.Create(w.GymId, w.MemberId, CheckinRejectionReason.NoMembership, eightPm, Day, null));
            await db.SaveChangesAsync();
        }

        var dash = await RunAsync(w, w.StaffId, UserRole.Staff, at: eightPm);

        Assert.Equal(1, dash.Indicators!.CheckInsToday);
        Assert.Equal(1, dash.Attention!.Rejected!.Total);
        Assert.Single(dash.Today!.RecentEntries!);
        var bucket = Assert.Single(dash.PeakHours!.Buckets);
        Assert.Equal(20, bucket.Hour); // las 20:00 locales, no la 01:00 UTC
    }

    // ---------- criterio 4: otro gym nunca aparece ----------

    [Fact]
    public async Task Another_gyms_data_never_appears()
    {
        var mine = await SeedAsync();
        var other = await SeedAsync();
        await SeedPendingVoidRequestAsync(other);
        await SeedOpenCashAsync(other, other.StaffId);
        await SeedOutOfServiceMachinesAsync(other);

        var clock = factory.Services.GetRequiredService<MutableClock>();
        var at = clock.BusinessDayRange(Day).Start.AddHours(10);
        using (var seed = factory.CreateScopeWithMigration())
        {
            seed.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            var db = seed.ServiceProvider.GetRequiredService<GsmDbContext>();
            db.Add(AttendanceEntity.Create(other.GymId, other.MemberId, AttMethod.Manual, at, Day));
            db.Add(CheckinAttempt.Create(other.GymId, other.MemberId, CheckinRejectionReason.NoMembership, at, Day, null));
            await db.SaveChangesAsync();
        }

        var dash = await RunAsync(mine, mine.OwnerId, UserRole.Owner);

        Assert.Equal(0, dash.Indicators!.CheckInsToday);
        Assert.Equal(0, dash.Attention!.Rejected!.Total);
        Assert.Equal(0, dash.Attention.VoidRequests!.Pending);
        Assert.Null(dash.Attention.Cash);
        Assert.Equal(0, dash.Attention.Equipment!.OutOfService);
        Assert.Empty(dash.Today!.RecentEntries!);
        Assert.Empty(dash.PeakHours!.Buckets);
    }

    // ---------- helpers ----------

    private async Task<DashboardResponse> RunAsync(
        World w, Guid userId, UserRole role, Action<MutableCurrentUser>? setup = null, DateTimeOffset? at = null)
    {
        using var scope = factory.CreateScopeWithMigration();
        Pin(scope, w, userId, role, at);
        setup?.Invoke(scope.ServiceProvider.GetRequiredService<MutableCurrentUser>());
        return await scope.ServiceProvider.GetRequiredService<GetDashboardHandler>().HandleAsync(default);
    }

    private static void Pin(IServiceScope scope, World w, Guid userId, UserRole role, DateTimeOffset? at)
    {
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(userId, w.GymId, role);
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
        if (at is { } instant)
        {
            clock.PinTo(instant, Day);
        }
        else
        {
            clock.PinToBusinessDay(Day);
        }
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Day);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Blocks Gym", $"blk-{Guid.NewGuid():N}"[..18]);
        var owner = User("owner");
        var staff = User("staff");
        var otherStaff = User("other");
        gym.AssignOwner(owner.Id);
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(owner);
        db.Add(staff);
        db.Add(otherStaff);

        var member = Member.Create(gym.Id, "Ana", "Prueba", $"doc-{Guid.NewGuid():N}"[..14], Day);
        db.Add(member);

        await db.SaveChangesAsync();
        return new World(gym.Id, owner.Id, staff.Id, otherStaff.Id, member.Id);
    }

    private async Task AssignTrainerAsync(World w, Guid trainerId)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var member = await db.Set<Member>().FindAsync(w.MemberId);
        member!.AssignTrainer(trainerId);
        await db.SaveChangesAsync();
    }

    private async Task SeedPendingVoidRequestAsync(World w)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var at = clock.BusinessDayRange(Day).Start.AddHours(9);

        var payment = Payment.Create(w.GymId, null, null, 10_000m, PaymentMethod.Card, "Pago", null, w.StaffId, at);
        db.Add(payment);
        db.Add(VoidRequest.Create(w.GymId, payment.Id, "Cobro duplicado", w.StaffId, at));
        await db.SaveChangesAsync();
    }

    private async Task SeedOpenCashAsync(World w, Guid openedBy)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        db.Add(CashShift.Open(w.GymId, openedBy, clock.BusinessDayRange(Day).Start.AddHours(8), 50_000m));
        await db.SaveChangesAsync();
    }

    private async Task SeedOutOfServiceMachinesAsync(World w)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var now = clock.BusinessDayRange(Day).Start.AddHours(9);

        var bench = EquipmentItem.Create(w.GymId, null, "Banca", EquipmentType.Bench, "Zona A", null, null, null, 1, null, null);
        var press = EquipmentItem.Create(w.GymId, null, "Prensa", EquipmentType.Machine, "Zona B", null, null, null, 1, null, null);
        bench.ChangeStatus(EquipmentStatus.OutOfService, "Tapicería rota", null, Day, w.StaffId, now);
        press.ChangeStatus(EquipmentStatus.OutOfService, "Falla de cable", null, Day, w.StaffId, now);
        db.Add(bench);
        db.Add(press);
        db.Add(EquipmentIssue.Report(
            w.GymId, press.Id, w.StaffId, IssueReporterRole.Staff, EquipmentIssueReason.Unsafe, "Se suelta el pasador"));
        await db.SaveChangesAsync();
    }

    private static GSM.Infrastructure.Identity.AppUser User(string name)
    {
        var email = $"{name}-{Guid.NewGuid():N}@blocks.local";
        return new()
        {
            UserName = email,
            Email = email,
            NormalizedUserName = email.ToUpperInvariant(),
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            FullName = $"Blocks {name}",
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
    }

    private sealed class ThrowingPaymentReader : IPaymentReader
    {
        public Task<decimal> GetMonthlyRevenueAsync(Guid gymId, int year, int month, CancellationToken cancellationToken = default)
            => throw Boom();

        public Task<decimal> GetRevenueBetweenDaysAsync(Guid gymId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken = default)
            => throw Boom();

        public Task<IReadOnlyDictionary<Guid, decimal>> GetMonthlyRevenueByGymAsync(int year, int month, CancellationToken cancellationToken = default)
            => throw Boom();

        public Task<decimal> GetNetPaidForMembershipAsync(Guid membershipId, CancellationToken cancellationToken = default)
            => throw Boom();

        public Task<IReadOnlyList<MemberPaymentView>> GetRecentByMemberAsync(Guid memberId, int limit, CancellationToken cancellationToken = default)
            => throw Boom();

        public Task<OpenCashShiftView?> GetOpenCashShiftAsync(CancellationToken cancellationToken = default)
            => throw Boom();

        public Task<int> GetPendingVoidRequestCountAsync(CancellationToken cancellationToken = default)
            => throw Boom();
    }

    private sealed class ThrowingEquipmentReader : IEquipmentReader
    {
        public Task<GymEquipmentAvailability> GetAvailabilityAsync(Guid gymId, CancellationToken cancellationToken = default)
            => throw Boom();

        public Task<IReadOnlyList<EquipmentItemInfo>> GetActiveItemsAsync(Guid gymId, CancellationToken cancellationToken = default)
            => throw Boom();

        public Task<IReadOnlyList<EquipmentModelInfo>> GetModelsAsync(IReadOnlyCollection<Guid> modelIds, CancellationToken cancellationToken = default)
            => throw Boom();

        public Task<EquipmentAttentionView> GetAttentionAsync(Guid gymId, int itemLimit, CancellationToken cancellationToken = default)
            => throw Boom();
    }

    private static InvalidOperationException Boom() => new("lector roto a propósito");
}
