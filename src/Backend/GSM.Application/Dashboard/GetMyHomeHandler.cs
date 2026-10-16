using GSM.Application.Achievements.Public;
using GSM.Application.Attendance.Public;
using GSM.Application.Classes.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Logs;
using GSM.Application.Communications.Public;
using GSM.Application.Gyms;
using GSM.Application.Gyms.Public;
using GSM.Application.MemberStreak;
using GSM.Application.Members.Public;
using GSM.Application.Memberships;
using GSM.Application.Memberships.Public;
using GSM.Application.Routines.Public;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Dashboard;

/// <summary>
/// Inicio del miembro (D2, <c>docs/GSM-dashboards.md</c> §2 y §4). Un endpoint, un response, ocho bloques. Compositor,
/// no módulo: lee solo por los <c>Public/I{Módulo}Reader</c> y solo lo del propio miembro (la identidad sale del
/// token). Un bloque apagado por el gym o cuyo lector lanza vuelve <c>null</c> (el error va a <c>logs.errors</c>) y
/// el resto se muestra. Los días salen de <see cref="IClock"/>.
/// </summary>
public sealed class GetMyHomeHandler(
    ICurrentUserService currentUser,
    IClock clock,
    ILogStore logStore,
    ILogger<GetMyHomeHandler> logger,
    IGymReader gyms,
    IMemberReader members,
    IMembershipReader memberships,
    IAttendanceReader attendance,
    IClassReader classes,
    IAchievementReader achievements,
    IRankingStandingReader ranking,
    IRoutineTodayReader routines,
    IMemberNewsReader news,
    GetMemberStreakHandler streak)
{
    /// <summary>La revisión de la rutina se avisa cuando faltan estos días o menos (o ya venció).</summary>
    public const int ReviewNoticeDays = 7;

    public async Task<MemberHomeResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        // Sin flags (dato faltante) los módulos opcionales se tratan como apagados: ante la duda, no se muestran.
        var flags = await RunAsync("flags", () => gyms.GetFeatureFlagsAsync(gymId, cancellationToken));

        return new MemberHomeResponse(
            Status: await RunAsync("status", () => StatusAsync(self.Id, cancellationToken)),
            Gym: await RunAsync("gym", () => GymAsync(gymId, cancellationToken)),
            Entry: await RunAsync("entry", () => EntryAsync(self.Id, cancellationToken)),
            Pending: await RunAsync("pending", () => PendingAsync(self.Id, flags, cancellationToken)),
            Training: flags is { ShowRoutines: true }
                ? await RunAsync("training", () => TrainingAsync(self.Id, cancellationToken))
                : null,
            NextClass: flags is { ShowClasses: true }
                ? await RunAsync("next-class", () => NextClassAsync(self.Id, cancellationToken))
                : null,
            Progress: await RunAsync("progress", () => ProgressAsync(self.Id, cancellationToken)),
            News: flags is { ShowCommunity: true }
                ? await RunAsync("news", () => NewsAsync(self.Id, cancellationToken))
                : null);
    }

    // 1. Saludo y estado

    private async Task<HomeStatusBlock?> StatusAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var access = MembershipMapper.ToResponse(await memberships.GetAccessStatusAsync(memberId, cancellationToken));
        var active = await memberships.GetActiveAsync(memberId, cancellationToken);
        return new HomeStatusBlock(access, active?.PlanName, active?.StartDate, active?.EndDate);
    }

    // 2. El gym ahora

    private async Task<HomeGymBlock?> GymAsync(Guid gymId, CancellationToken cancellationToken)
    {
        var open = await gyms.GetOpenStatusAsync(gymId, clock.UtcNow, cancellationToken);
        var openStatus = OpeningHoursSupport.ToDto(open, clock.Today);

        // La ocupación es un extra del bloque: si falla, el abierto/cerrado se muestra igual.
        var level = await RunAsync("gym-occupancy", async () =>
        {
            var view = await attendance.GetOccupancyAsync(gymId, cancellationToken);
            return view.Level is null ? null : new OccupancyLevelHolder(view.Level);
        });

        return new HomeGymBlock(openStatus, level?.Level);
    }

    private sealed record OccupancyLevelHolder(string Level);

    // 3. Entrada

    private async Task<HomeEntryBlock?> EntryAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var at = await attendance.GetFirstEntryTodayAsync(memberId, cancellationToken);
        return new HomeEntryBlock(at is not null, at);
    }

    // 4. Pendientes

    private async Task<HomePendingBlock?> PendingAsync(
        Guid memberId, GymFeatureFlagsView? flags, CancellationToken cancellationToken)
    {
        // Cada parte es independiente: una que falla o está apagada queda vacía, la otra se muestra.
        var offers = flags is { ShowClasses: true }
            ? await RunAsync("pending-waitlist", async () =>
                (IReadOnlyList<HomeWaitlistOffer>)(await classes.GetPendingWaitlistOffersAsync(memberId, cancellationToken))
                    .Select(o => new HomeWaitlistOffer(o.ClassId, o.ClassName, o.SessionDate, o.StartTime, o.ExpiresAt))
                    .ToList())
            : null;

        var rewards = flags is { ShowGamification: true }
            ? await RunAsync("pending-rewards", async () =>
                (IReadOnlyList<HomeReward>)(await achievements.GetPendingRewardsAsync(memberId, cancellationToken))
                    .Select(r => new HomeReward(r.AchievementId, r.AchievementName, r.RewardDescription, r.ExpiresAt))
                    .ToList())
            : null;

        return new HomePendingBlock(offers ?? [], rewards ?? []);
    }

    // 5. Entrenamiento de hoy

    private async Task<HomeTrainingBlock?> TrainingAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var today = await routines.GetAsync(memberId, cancellationToken);
        if (today is null)
        {
            return null;
        }

        int? reviewDaysLeft = null;
        DateOnly? reviewOn = null;
        if (today.ReviewOn is { } review)
        {
            var left = review.DayNumber - clock.Today.DayNumber;
            if (left <= ReviewNoticeDays)
            {
                reviewOn = review;
                reviewDaysLeft = left;
            }
        }

        return new HomeTrainingBlock(
            today.RoutineName, today.DayName, today.IsRest, today.ExerciseCount, today.MuscleGroups, reviewOn, reviewDaysLeft);
    }

    // 6. Próxima clase

    private async Task<HomeNextClassBlock?> NextClassAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var next = (await classes.GetUpcomingBookingsAsync(memberId, 1, cancellationToken)).FirstOrDefault();
        return next is null
            ? null
            : new HomeNextClassBlock(next.ClassId, next.ClassName, next.SessionDate, next.StartTime, next.InstructorName);
    }

    // 7. Progreso

    private async Task<HomeProgressBlock?> ProgressAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var current = await streak.HandleAsync(memberId, cancellationToken);

        // El ranking es un extra del bloque: si falla o está apagado, racha y congeladores se muestran igual.
        var standing = await RunAsync("progress-ranking", async () =>
        {
            var view = await ranking.GetAsync(memberId, cancellationToken);
            return view is null ? null : new HomeRankingStanding(view.Participating, view.Position, view.Days);
        });

        return new HomeProgressBlock(current.CurrentStreak, current.FreezesAvailable, current.TotalVisits, standing);
    }

    // 8. Novedades

    private async Task<HomeNewsBlock?> NewsAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var view = await news.GetAsync(memberId, cancellationToken);
        return new HomeNewsBlock(
            view.Featured is { } f ? new HomeFeaturedAnnouncement(f.Id, f.Title, f.Pinned, f.Important, f.PublishedAt) : null,
            view.UnreadAnnouncements);
    }

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
            logger.LogError(ex, "Inicio del miembro: el bloque {Block} falló y se omite", block);
            logStore.RecordError(new ErrorLogEntry(
                ErrorSource.Api, ex, Method: "GET", Path: "/api/me/home", Endpoint: $"me/home/{block}"));
            return null;
        }
    }
}
