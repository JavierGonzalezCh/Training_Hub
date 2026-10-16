using GSM.Application.Attendance.Contracts;
using GSM.Application.Attendance.Public;
using GSM.Application.Classes.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Logs;
using GSM.Application.Equipment.Public;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Public;
using GSM.Application.Payments.Public;
using GSM.Application.Routines;
using GSM.Application.Routines.Public;
using GSM.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Dashboard;

/// <summary>
/// Panel del gym (D1). Un endpoint, un response, por bloques (<c>docs/GSM-dashboards.md</c> §1 y §4). Es un
/// compositor, no un módulo: lee solo por los <c>Public/I{Módulo}Reader</c> y no escribe nada.
/// <list type="bullet">
/// <item>Un bloque que quien entra no puede ver vuelve <c>null</c> (no 403).</item>
/// <item>Un bloque cuyo lector lanza vuelve <c>null</c> con el error en <c>logs.errors</c>; el resto llega.</item>
/// <item>Días y meses de negocio con <see cref="IClock"/>, nunca rangos UTC.</item>
/// </list>
/// </summary>
public sealed class GetDashboardHandler(
    ICurrentUserService currentUser,
    IClock clock,
    ILogStore logStore,
    ILogger<GetDashboardHandler> logger,
    IGymReader gyms,
    IMemberReader members,
    IAttendanceReader attendance,
    IPaymentReader payments,
    IMembershipReader memberships,
    IEquipmentReader equipment,
    IClassReader classes,
    IRoutineReader routines)
{
    /// <summary>Ventana de la afluencia por hora (el panel no recibe filtros).</summary>
    public const int PeakWindowDays = 30;

    private const int ListLimit = 8;
    private const int ExpiringLimit = 10;

    public async Task<DashboardResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var flags = await RunAsync("flags", () => gyms.GetFeatureFlagsAsync(gymId, cancellationToken));

        // Las sesiones de hoy las usan "Hoy" y "Mis miembros": se leen una sola vez.
        IReadOnlyList<TodaySessionView>? sessions = null;
        var sessionsRead = false;

        async Task<IReadOnlyList<TodaySessionView>?> TodaySessionsAsync()
        {
            if (flags is { ShowClasses: false })
            {
                return null;
            }

            if (!sessionsRead)
            {
                sessionsRead = true;
                sessions = await RunAsync<IReadOnlyList<TodaySessionView>>("classes", async () => await classes.GetTodaySessionsAsync(gymId, cancellationToken));
            }

            return sessions;
        }

        return new DashboardResponse(
            Indicators: await RunAsync("indicators", () => IndicatorsAsync(gymId, cancellationToken)),
            Finances: await RunAsync("finances", () => FinancesAsync(gymId, cancellationToken)),
            Attention: await RunAsync("attention", () => AttentionAsync(gymId, cancellationToken)),
            Today: await RunAsync("today", async () => await TodayAsync(gymId, await TodaySessionsAsync(), cancellationToken)),
            PeakHours: await RunAsync("peak-hours", () => PeakHoursAsync(cancellationToken)),
            Expiring: await RunAsync("expiring", () => ExpiringAsync(gymId, cancellationToken)),
            MyMembers: await RunAsync(
                "my-members", async () => await MyMembersAsync(await TodaySessionsAsync(), flags, cancellationToken)));
    }

    // --- Indicadores (todos) ---

    private async Task<IndicatorsBlock?> IndicatorsAsync(Guid gymId, CancellationToken cancellationToken)
    {
        var settings = await memberships.GetSettingsAsync(gymId, cancellationToken);

        // Solo miembros activos: los dados de baja conservan sus membresías pero no cuentan aquí.
        var activeIds = await members.GetActiveMemberIdsAsync(gymId, cancellationToken);
        var counts = await memberships.GetStatusCountsAsync(gymId, activeIds, cancellationToken);

        var occupancy = await RunAsync("occupancy", async () =>
        {
            var view = await attendance.GetOccupancyAsync(gymId, cancellationToken);
            return new OccupancyResponse(view.PeopleInside, view.Capacity, view.Percent, view.Level, view.AssumedStayMinutes);
        });

        return new IndicatorsBlock(
            ActiveMembers: await members.GetActiveCountAsync(gymId, cancellationToken),
            CheckInsToday: await attendance.GetTodayCountAsync(gymId, cancellationToken),
            Occupancy: occupancy,
            CommercialStatus: new CommercialStatusCounts(
                counts[CommercialStatus.Current],
                counts[CommercialStatus.Expiring],
                counts[CommercialStatus.Grace],
                counts[CommercialStatus.Expired],
                counts[CommercialStatus.Frozen],
                counts[CommercialStatus.NoPlan],
                counts[CommercialStatus.Scheduled]),
            ExpiringDays: settings.ExpiringDays);
    }

    // --- Ingresos (Owner y ViewFinances) ---

    private async Task<FinancesBlock?> FinancesAsync(Guid gymId, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(StaffPermission.ViewFinances))
        {
            return null;
        }

        var today = clock.Today;
        var lastMonth = today.AddMonths(-1);

        // B2: el mes en curso va por el día de hoy, así que se compara con los mismos días del mes anterior
        // (del 1 al mismo día; si el mes anterior es más corto, hasta su último día), no con el mes completo.
        var sameDaysLastMonth = await payments.GetRevenueBetweenDaysAsync(
            gymId,
            new DateOnly(lastMonth.Year, lastMonth.Month, 1),
            new DateOnly(lastMonth.Year, lastMonth.Month, Math.Min(today.Day, DateTime.DaysInMonth(lastMonth.Year, lastMonth.Month))),
            cancellationToken);

        return new FinancesBlock(
            await payments.GetMonthlyRevenueAsync(gymId, today.Year, today.Month, cancellationToken),
            sameDaysLastMonth);
    }

    // --- Requiere atención ---

    private async Task<AttentionBlock?> AttentionAsync(Guid gymId, CancellationToken cancellationToken)
    {
        var equipmentBlock = await RunAsync("attention-equipment", async () =>
        {
            var view = await equipment.GetAttentionAsync(gymId, ListLimit, cancellationToken);
            return new EquipmentAttentionBlock(
                view.OutOfService,
                view.InMaintenance,
                view.OpenIssues,
                view.UrgentIssues,
                view.OutOfServiceItems
                    .Select(i => new OutOfServiceMachineRow(i.Id, i.Name, i.Zone, i.ExpectedBackOn, i.HasUnsafeIssue))
                    .ToList());
        });

        var rejected = await RunAsync("attention-rejected", async () =>
        {
            var view = await attendance.GetRejectedTodayAsync(ListLimit, cancellationToken);
            var names = await members.GetFullNamesAsync(view.Items.Select(i => i.MemberId).Distinct().ToList(), cancellationToken);
            return new RejectedTodayBlock(
                view.Total,
                view.Unseen,
                view.Items
                    .Select(i => new RejectedAttemptRow(
                        i.MemberId, names.GetValueOrDefault(i.MemberId, "?"), i.AttemptedAt, i.AttemptCount, i.Reason, i.Seen))
                    .ToList());
        });

        var voidRequests = currentUser.HasPermission(StaffPermission.VoidPayments)
            ? await RunAsync(
                "attention-void-requests",
                async () => new VoidRequestsBlock(await payments.GetPendingVoidRequestCountAsync(cancellationToken)))
            : null;

        var cash = await RunAsync("attention-cash", () => CashAsync(cancellationToken));

        return new AttentionBlock(equipmentBlock, rejected, voidRequests, cash);
    }

    /// <summary>
    /// La caja abierta: el Owner y quien tiene <c>ViewFinances</c> ven la de cualquiera (y si quedó olvidada);
    /// el resto, solo la suya.
    /// </summary>
    private async Task<CashBlock?> CashAsync(CancellationToken cancellationToken)
    {
        var open = await payments.GetOpenCashShiftAsync(cancellationToken);
        if (open is null)
        {
            return null;
        }

        var mine = currentUser.UserId is { } userId && open.OpenedBy == userId;
        if (!mine && !currentUser.HasPermission(StaffPermission.ViewFinances))
        {
            return null;
        }

        return new CashBlock(open.ShiftId, open.OpenedByName, open.OpenedAt, open.ExpectedCash, open.IsStale, mine);
    }

    // --- Hoy ---

    private async Task<TodayBlock?> TodayAsync(
        Guid gymId, IReadOnlyList<TodaySessionView>? sessions, CancellationToken cancellationToken)
    {
        var birthdays = await RunAsync("today-birthdays", async () =>
            (IReadOnlyList<BirthdayTodayRow>)(await members.GetBirthdaysOnAsync(gymId, clock.Today, cancellationToken))
                .Select(b => new BirthdayTodayRow(b.Id, b.FullName))
                .ToList());

        var recent = await RunAsync("today-recent-entries", async () =>
        {
            var entries = await attendance.GetRecentEntriesTodayAsync(ListLimit, cancellationToken);
            var names = await members.GetFullNamesAsync(entries.Select(e => e.MemberId).Distinct().ToList(), cancellationToken);
            return (IReadOnlyList<RecentEntryRow>)entries
                .Select(e => new RecentEntryRow(e.MemberId, names.GetValueOrDefault(e.MemberId, "?"), e.CheckedInAt, e.Method))
                .ToList();
        });

        return new TodayBlock(sessions?.Select(ToRow).ToList(), birthdays, recent);
    }

    private static TodayClassRow ToRow(TodaySessionView s)
        => new(
            s.ClassId, s.ClassName, s.StartTime, s.DurationMinutes, s.InstructorName,
            Cancelled: s.Status == "Cancelled", s.Capacity, s.Booked, s.Waiting);

    // --- Afluencia por hora ---

    private async Task<PeakHoursBlock?> PeakHoursAsync(CancellationToken cancellationToken)
    {
        var counts = await attendance.GetHourlyCountsAsync(PeakWindowDays, cancellationToken);
        return new PeakHoursBlock(PeakWindowDays, counts.Select(c => new HourBucket(c.Hour, c.Count)).ToList());
    }

    // --- Por vencer y en gracia ---

    private async Task<ExpiringBlock?> ExpiringAsync(Guid gymId, CancellationToken cancellationToken)
    {
        var settings = await memberships.GetSettingsAsync(gymId, cancellationToken);
        var expiring = await memberships.GetExpiringAsync(gymId, settings.ExpiringDays, cancellationToken);

        // En gracia: vencidas que aún entran. Los candidatos de recordatorio incluyen renovadas por otra
        // membresía, así que se confirma el estado comercial del miembro hoy.
        var candidates = (await memberships.GetReminderCandidatesAsync(gymId, settings.ExpiringDays, cancellationToken))
            .Where(c => c.DaysLeft < 0)
            .ToList();
        var statuses = candidates.Count == 0
            ? new Dictionary<Guid, CommercialStatus>()
            : (await memberships.GetCommercialStatusesAsync(candidates.Select(c => c.MemberId).Distinct().ToList(), cancellationToken))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        var inGrace = candidates
            .Where(c => statuses.GetValueOrDefault(c.MemberId) == CommercialStatus.Grace)
            .Select(c => new ExpiringMembershipView(c.MembershipId, c.MemberId, c.PlanName, c.EndDate, c.DaysLeft))
            .ToList();

        var shown = expiring.Concat(inGrace)
            .OrderBy(e => e.DaysLeft)
            .Take(ExpiringLimit)
            .ToList();

        var names = await members.GetFullNamesAsync(shown.Select(e => e.MemberId).Distinct().ToList(), cancellationToken);
        var rows = shown
            .Select(e => new ExpiringMemberRow(
                e.MembershipId, e.MemberId, names.GetValueOrDefault(e.MemberId, "(miembro no encontrado)"),
                e.PlanName, e.EndDate, e.DaysLeft))
            .ToList();

        return new ExpiringBlock(
            expiring.Count,
            await memberships.GetExpiredCountAsync(gymId, cancellationToken),
            settings.ExpiringDays,
            rows);
    }

    // --- Mis miembros (Trainer) ---

    private async Task<MyMembersBlock?> MyMembersAsync(
        IReadOnlyList<TodaySessionView>? sessions, GymFeatureFlagsView? flags, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(StaffPermission.Trainer) || currentUser.UserId is not { } userId)
        {
            return null;
        }

        var mine = await members.GetActiveMemberIdsByTrainerAsync(userId, cancellationToken);
        var myClasses = (sessions ?? [])
            .Where(s => s.InstructorUserId == userId)
            .Select(ToRow)
            .ToList();

        var routinesOn = flags is not { ShowRoutines: false };
        var due = routinesOn && mine.Count > 0
            ? await routines.GetDueForReviewCountAsync(mine, ListRoutinesDueForReviewHandler.WindowDays, cancellationToken)
            : new DueRoutinesCount(0, 0);

        var without = new List<Guid>();
        if (routinesOn && mine.Count > 0)
        {
            var withRoutine = await routines.GetMemberIdsWithActiveRoutineAsync(mine, cancellationToken);
            without = mine.Where(id => !withRoutine.Contains(id)).ToList();
        }

        var names = await members.GetFullNamesAsync(without.Take(5).ToList(), cancellationToken);
        var block = new MyMembersBlock(
            myClasses,
            due.Due,
            due.Overdue,
            without.Count,
            without.Take(5).Select(id => new MemberNameRow(id, names.GetValueOrDefault(id, "?"))).ToList());

        // El Owner cumple todos los permisos pero no es entrenador de nadie: sin nada que mostrar, el bloque no va.
        var isEmpty = myClasses.Count == 0 && due.Due == 0 && without.Count == 0;
        return isEmpty && (currentUser.Role == UserRole.Owner || currentUser.IsImpersonating) ? null : block;
    }

    // --- Un bloque que falla no tumba el panel ---

    private async Task<T?> RunAsync<T>(string block, Func<Task<T?>> read) where T : class
    {
        try
        {
            return await read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dashboard: el bloque {Block} falló y se omite", block);
            logStore.RecordError(new ErrorLogEntry(
                ErrorSource.Api, ex, Method: "GET", Path: "/api/dashboard", Endpoint: $"dashboard/{block}"));
            return null;
        }
    }
}
