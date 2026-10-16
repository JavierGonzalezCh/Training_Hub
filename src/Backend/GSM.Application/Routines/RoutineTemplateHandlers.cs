using System.Text.Json;
using GSM.Application.Common.Abstractions;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Routines;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Routines;

/// <summary>Utilidades de las plantillas (R11): serializar el contenido, validarlo y leerlo de vuelta.</summary>
internal static class RoutineTemplating
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record Prepared(string Content, string Mode, int DayCount, int ExerciseCount);

    /// <summary>
    /// Valida el cuerpo igual que al asignar una rutina (ejercicios visibles, series coherentes con el
    /// <c>tracking_type</c>, reglas del calendario) con una asignación de prueba que no se guarda, y devuelve el
    /// documento a persistir: el mismo cuerpo, sin ids de días ni de líneas.
    /// </summary>
    public static async Task<Prepared> PrepareAsync(
        IExerciseRepository exercises, IClock clock, Guid gymId, SaveRoutineRequest request,
        CancellationToken cancellationToken)
    {
        var catalog = await RoutineDrafting.ValidateExercisesVisibleAsync(exercises, request, cancellationToken);
        var draft = RoutineDrafting.ToDraft(request, catalog, Guid.Empty, selfAssigned: false);
        var trial = Routine.Assign(gymId, Guid.CreateVersion7(), clock.Today, draft);

        var stripped = request with
        {
            Days = (request.Days ?? []).Select(d => d with
            {
                Id = null,
                Exercises = (d.Exercises ?? []).Select(e => e with { Id = null }).ToList(),
            }).ToList(),
        };

        return new Prepared(
            JsonSerializer.Serialize(stripped, Json),
            RoutineSchedule.ModeToText(trial.ScheduleMode),
            stripped.Days.Count(d => !d.IsRest),
            stripped.Days.Sum(d => d.Exercises.Count));
    }

    public static SaveRoutineRequest ReadContent(RoutineTemplate template)
        => JsonSerializer.Deserialize<SaveRoutineRequest>(template.Content, Json)
           ?? throw new ValidationDomainException("La plantilla está vacía.");

    public static RoutineTemplateListItem ToListItem(RoutineTemplate t)
        => new(t.Id, t.Name, t.Description, t.ScheduleMode, t.DayCount, t.ExerciseCount, t.CreatedAt);

    /// <summary>Convierte una rutina existente en el cuerpo de una plantilla (nombres y ids de ejercicio, series, grupos).</summary>
    public static SaveRoutineRequest FromResponse(RoutineResponse routine)
        => new(
            routine.Name,
            routine.Notes,
            routine.Days.OrderBy(d => d.Position).Select(d => new RoutineDayInput(
                d.Name,
                d.IsRest,
                d.Exercises.OrderBy(e => e.Order).Select(e => new RoutineExerciseInput(
                    e.ExerciseId,
                    e.SetTargets.OrderBy(t => t.Number).Select(t => new SetTargetInput(
                        t.SetType, t.RepsMin, t.RepsMax, t.WeightKg, t.DurationSeconds, t.DistanceM, t.Rir)).ToList(),
                    e.RestSeconds,
                    e.Notes,
                    e.Group)).ToList(),
                null,
                d.DayOfWeek)).ToList(),
            routine.ScheduleMode);
}

public sealed class ListRoutineTemplatesHandler(ICurrentUserService currentUser, IRoutineTemplateRepository templates)
{
    public async Task<IReadOnlyList<RoutineTemplateListItem>> HandleAsync(CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        return (await templates.ListAsync(cancellationToken)).Select(RoutineTemplating.ToListItem).ToList();
    }
}

public sealed class GetRoutineTemplateHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IRoutineTemplateRepository templates,
    IExerciseRepository exercises,
    ExerciseEquipmentResolver equipment)
{
    public async Task<RoutineTemplateResponse> HandleAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var template = await templates.GetAsync(templateId, cancellationToken)
            ?? throw new NotFoundException("Plantilla no encontrada.");

        var content = RoutineTemplating.ReadContent(template);

        // Sin validar visibilidad: una plantilla con ejercicios que ya no están disponibles debe poder abrirse para arreglarla.
        var ids = content.Days.SelectMany(d => d.Exercises ?? []).Select(e => e.ExerciseId).Distinct().ToList();
        var visible = ids.Count == 0
            ? new Dictionary<Guid, ExerciseSummary>()
            : (await exercises.GetVisibleSummariesAsync(ids, cancellationToken)).ToDictionary(x => x.Key, x => x.Value);
        var unavailable = content.Days.SelectMany(d => d.Exercises ?? []).Count(e => !visible.ContainsKey(e.ExerciseId));

        var draft = RoutineDrafting.ToDraft(content, visible, Guid.Empty, selfAssigned: false);
        var shape = Routine.Assign(gymId, Guid.CreateVersion7(), clock.Today, draft);
        var machines = await equipment.ResolveAsync(RoutineMapper.EquipmentNeeds(shape, visible), cancellationToken);
        var routine = RoutineMapper.ToResponse(shape, visible, machines) with { Id = template.Id };

        return new RoutineTemplateResponse(
            template.Id, template.Name, template.Description, template.ScheduleMode,
            template.DayCount, template.ExerciseCount, unavailable, routine, template.CreatedAt);
    }
}

public sealed class CreateRoutineTemplateHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IExerciseRepository exercises,
    IRoutineTemplateRepository templates,
    IUnitOfWork unitOfWork,
    ILogger<CreateRoutineTemplateHandler> logger)
{
    public async Task<RoutineTemplateListItem> HandleAsync(
        SaveRoutineTemplateRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var prepared = await RoutineTemplating.PrepareAsync(exercises, clock, gymId, request.Routine, cancellationToken);

        var template = RoutineTemplate.Create(
            gymId, request.Name, request.Description, prepared.Mode, prepared.Content,
            prepared.DayCount, prepared.ExerciseCount, currentUser.UserId ?? Guid.Empty);
        templates.Add(template);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Plantilla de rutina {TemplateId} creada ({Days} días)", template.Id, prepared.DayCount);
        return RoutineTemplating.ToListItem(template);
    }
}

public sealed class SaveRoutineAsTemplateHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IExerciseRepository exercises,
    ExerciseEquipmentResolver equipment,
    IRoutineRepository routines,
    IRoutineTemplateRepository templates,
    IUnitOfWork unitOfWork,
    ILogger<SaveRoutineAsTemplateHandler> logger)
{
    public async Task<RoutineTemplateListItem> HandleAsync(
        Guid routineId, SaveRoutineAsTemplateRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var routine = await routines.GetWithDaysAsync(routineId, cancellationToken)
            ?? throw new NotFoundException("Rutina no encontrada.");

        var response = await RoutineDrafting.BuildResponseAsync(exercises, equipment, routine, cancellationToken);
        var prepared = await RoutineTemplating.PrepareAsync(
            exercises, clock, gymId, RoutineTemplating.FromResponse(response), cancellationToken);

        var template = RoutineTemplate.Create(
            gymId, request.Name, request.Description, prepared.Mode, prepared.Content,
            prepared.DayCount, prepared.ExerciseCount, currentUser.UserId ?? Guid.Empty);
        templates.Add(template);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Rutina {RoutineId} guardada como plantilla {TemplateId}", routineId, template.Id);
        return RoutineTemplating.ToListItem(template);
    }
}

public sealed class UpdateRoutineTemplateHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IExerciseRepository exercises,
    IRoutineTemplateRepository templates,
    IUnitOfWork unitOfWork,
    ILogger<UpdateRoutineTemplateHandler> logger)
{
    public async Task<RoutineTemplateListItem> HandleAsync(
        Guid templateId, SaveRoutineTemplateRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var template = await templates.GetAsync(templateId, cancellationToken)
            ?? throw new NotFoundException("Plantilla no encontrada.");

        var prepared = await RoutineTemplating.PrepareAsync(exercises, clock, gymId, request.Routine, cancellationToken);
        template.Update(
            request.Name, request.Description, prepared.Mode, prepared.Content, prepared.DayCount, prepared.ExerciseCount);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Plantilla de rutina {TemplateId} actualizada", templateId);
        return RoutineTemplating.ToListItem(template);
    }
}

public sealed class DeleteRoutineTemplateHandler(
    ICurrentUserService currentUser,
    IRoutineTemplateRepository templates,
    IUnitOfWork unitOfWork,
    ILogger<DeleteRoutineTemplateHandler> logger)
{
    /// <summary>Borra la plantilla; las rutinas que salieron de ella no dependen de ella y quedan intactas.</summary>
    public async Task HandleAsync(Guid templateId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var template = await templates.GetAsync(templateId, cancellationToken)
            ?? throw new NotFoundException("Plantilla no encontrada.");

        templates.Remove(template);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Plantilla de rutina {TemplateId} eliminada", templateId);
    }
}

/// <summary>
/// Asigna la plantilla a un miembro: copia su contenido como una rutina nueva por el mismo camino que la asignación
/// directa (desactiva la activa, fecha de revisión, evento <c>RoutineAssigned</c>). Si algún ejercicio ya no está
/// disponible para el gym responde 422 y hay que corregir la plantilla.
/// </summary>
public sealed class AssignRoutineFromTemplateHandler(
    IRoutineTemplateRepository templates,
    AssignRoutineHandler assign,
    ILogger<AssignRoutineFromTemplateHandler> logger)
{
    public async Task<RoutineResponse> HandleAsync(Guid memberId, Guid templateId, CancellationToken cancellationToken)
    {
        var template = await templates.GetAsync(templateId, cancellationToken)
            ?? throw new NotFoundException("Plantilla no encontrada.");

        var content = RoutineTemplating.ReadContent(template);
        logger.LogInformation("Asignando la plantilla {TemplateId} al miembro {MemberId}", templateId, memberId);
        return await assign.HandleAsync(memberId, content, cancellationToken);
    }
}
