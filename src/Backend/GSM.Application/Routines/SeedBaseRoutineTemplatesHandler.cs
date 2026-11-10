using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms.Events;
using GSM.Application.Gyms.Public;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Routines;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Routines;

/// <summary>
/// Copia las plantillas base (<see cref="RoutineBaseTemplates"/>) a un gym (RT1). Idempotente: no vuelve a crear una
/// cuyo <c>BaseKey</c> el gym ya tenga. Corre sin usuario (alta del gym, relleno), así que no depende del filtro global.
/// </summary>
public sealed class SeedBaseRoutineTemplatesHandler(
    IExerciseRepository exercises,
    IRoutineTemplateRepository templates,
    IUnitOfWork unitOfWork,
    ILogger<SeedBaseRoutineTemplatesHandler> logger)
{
    /// <returns>Cuántas plantillas creó.</returns>
    public async Task<int> HandleAsync(Guid gymId, CancellationToken cancellationToken)
    {
        var existing = await templates.GetBaseKeysAsync(gymId, cancellationToken);
        var pending = RoutineBaseTemplates.All.Where(t => !existing.Contains(t.Key)).ToList();
        if (pending.Count == 0)
        {
            return 0;
        }

        var names = pending.SelectMany(t => t.Days).SelectMany(d => d.Lines).Select(l => l.Exercise).Distinct().ToList();
        var ids = await exercises.GetSystemIdsByNameAsync(names, cancellationToken);
        foreach (var missing in names.Where(n => !ids.ContainsKey(n)))
        {
            logger.LogWarning("Plantillas base: el ejercicio {Exercise} no está en el catálogo del sistema; se omite", missing);
        }

        var created = 0;
        foreach (var definition in pending)
        {
            var request = ToRequest(definition, ids);
            if (request.Days.Count == 0)
            {
                continue;
            }

            templates.Add(RoutineTemplate.CreateFromBase(
                gymId, definition.Key, definition.Name, definition.Description, "rotating",
                RoutineTemplating.Serialize(request), request.Days.Count, request.Days.Sum(d => d.Exercises.Count),
                definition.ImageUrl, Guid.Empty));
            created++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Plantillas base de rutina sembradas en el gym {GymId}: {Count}", gymId, created);
        return created;
    }

    private static SaveRoutineRequest ToRequest(
        RoutineBaseTemplates.Template definition, IReadOnlyDictionary<string, Guid> ids)
        => new(
            definition.Name,
            definition.Description,
            definition.Days
                .Select(d => new RoutineDayInput(
                    d.Name,
                    false,
                    d.Lines.Where(l => ids.ContainsKey(l.Exercise)).Select(l => new RoutineExerciseInput(
                        ids[l.Exercise],
                        l.Sets.Select(s => new SetTargetInput(
                            "normal", s.RepsMin, s.RepsMax, null, s.DurationSeconds, null, null)).ToList(),
                        l.RestSeconds,
                        l.Notes)).ToList()))
                .Where(d => d.Exercises.Count > 0)
                .ToList(),
            "rotating");
}

/// <summary>Al crear un gym le copia las plantillas base de rutina (RT1).</summary>
public sealed class GymCreatedSeedsBaseRoutineTemplatesHandler(SeedBaseRoutineTemplatesHandler seeder)
    : IDomainEventHandler<GymCreated>
{
    public Task HandleAsync(GymCreated domainEvent, CancellationToken cancellationToken)
        => seeder.HandleAsync(domainEvent.GymId, cancellationToken);
}

/// <summary>
/// Relleno de una sola vez para los gyms que ya existían antes de RT1 (<c>POST /internal/jobs/routines/seed-base-templates</c>).
/// No va en la tarea periódica: una plantilla base que el gym borró volvería a aparecer.
/// </summary>
public sealed class SeedBaseRoutineTemplatesForAllGymsHandler(
    IGymReader gyms,
    SeedBaseRoutineTemplatesHandler seeder,
    ILogger<SeedBaseRoutineTemplatesForAllGymsHandler> logger)
{
    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        foreach (var gym in await gyms.ListStartupStatesAsync(cancellationToken))
        {
            total += await seeder.HandleAsync(gym.GymId, cancellationToken);
        }

        logger.LogInformation("Relleno de plantillas base de rutina: {Count} creadas", total);
        return total;
    }
}
