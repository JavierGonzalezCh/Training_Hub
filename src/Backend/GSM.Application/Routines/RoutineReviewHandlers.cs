using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Public;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;
using GSM.Domain.Routines;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Routines;

internal static class RoutineSettingsLookup
{
    /// <summary>Semanas de revisión del gym; sin fila valen las de <see cref="RoutineSettings.Default"/>.</summary>
    public static async Task<int> GetReviewWeeksAsync(
        IRoutineRepository routines, Guid gymId, CancellationToken cancellationToken)
        => (await routines.GetSettingsAsync(gymId, cancellationToken))?.ReviewWeeks
           ?? RoutineSettings.DefaultReviewWeeks;
}

public sealed class GetRoutineSettingsHandler(ICurrentUserService currentUser, IRoutineRepository routines)
{
    public async Task<RoutineSettingsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        return new RoutineSettingsResponse(
            await RoutineSettingsLookup.GetReviewWeeksAsync(routines, gymId, cancellationToken));
    }
}

/// <summary>
/// Ajustes de rutinas del gym. Sin fila valen los valores por defecto; la primera edición la crea. Cambiar las
/// semanas no mueve las fechas de revisión ya fijadas: aplican a las rutinas que se asignen o revisen desde ahora.
/// </summary>
public sealed class UpdateRoutineSettingsHandler(
    ICurrentUserService currentUser,
    IRoutineRepository routines,
    IUnitOfWork unitOfWork,
    ILogger<UpdateRoutineSettingsHandler> logger)
{
    public async Task<RoutineSettingsResponse> HandleAsync(
        UpdateRoutineSettingsRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var current = await routines.GetSettingsAsync(gymId, cancellationToken);
        if (current is null)
        {
            current = RoutineSettings.Default(gymId);
            routines.Add(current);
        }

        current.Update(request.ReviewWeeks);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Ajustes de rutinas del gym {GymId}: revisión cada {Weeks} semanas", gymId, current.ReviewWeeks);
        return new RoutineSettingsResponse(current.ReviewWeeks);
    }
}

/// <summary>
/// Rutinas activas con fecha de revisión vencida o dentro de los próximos <see cref="WindowDays"/> días. Si
/// <c>mine</c> no viene, el entrenador (staff con el permiso <c>Trainer</c>) ve por defecto las de sus miembros
/// asignados; el Owner y el resto de staff ven todas. Solo miembros activos y rutinas armadas por el staff.
/// </summary>
public sealed class ListRoutinesDueForReviewHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IRoutineRepository routines)
{
    public const int WindowDays = 7;

    public async Task<DueRoutinesResponse> HandleAsync(bool? mine, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var onlyMine = mine ?? (currentUser.Role == UserRole.Staff && currentUser.HasPermission(StaffPermission.Trainer));

        IReadOnlyList<Guid> memberIds;
        if (onlyMine)
        {
            memberIds = currentUser.UserId is { } userId
                ? await members.GetActiveMemberIdsByTrainerAsync(userId, cancellationToken)
                : [];
        }
        else
        {
            memberIds = await members.GetActiveMemberIdsAsync(gymId, cancellationToken);
        }

        if (memberIds.Count == 0)
        {
            return new DueRoutinesResponse([], onlyMine, WindowDays);
        }

        var due = await routines.ListDueForReviewAsync(clock.Today.AddDays(WindowDays), memberIds, cancellationToken);
        var names = await members.GetFullNamesAsync(due.Select(r => r.MemberId).Distinct().ToList(), cancellationToken);

        var items = due.Select(r => new RoutineDueItem(
            r.Id,
            r.MemberId,
            names.GetValueOrDefault(r.MemberId, "?"),
            r.Name,
            r.StartsOn,
            r.ReviewOn,
            r.LastReviewedAt)).ToList();

        return new DueRoutinesResponse(items, onlyMine, WindowDays);
    }
}

/// <summary>Marca la rutina como revisada y corre su fecha de revisión las semanas configuradas en el gym.</summary>
public sealed class ReviewRoutineHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IRoutineRepository routines,
    IUnitOfWork unitOfWork,
    ILogger<ReviewRoutineHandler> logger)
{
    public async Task<RoutineReviewResponse> HandleAsync(Guid routineId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var routine = await routines.GetWithDaysAsync(routineId, cancellationToken)
            ?? throw new NotFoundException("Rutina no encontrada.");

        var weeks = await RoutineSettingsLookup.GetReviewWeeksAsync(routines, gymId, cancellationToken);
        var now = clock.UtcNow;
        routine.MarkReviewed(
            currentUser.UserId ?? throw new ForbiddenDomainException("Hace falta una cuenta para revisar la rutina."),
            now, clock.Today, weeks);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Rutina {RoutineId} revisada por {UserId}; próxima revisión {ReviewOn}",
            routine.Id, currentUser.UserId, routine.ReviewOn);
        return new RoutineReviewResponse(routine.Id, routine.ReviewOn, now);
    }
}
