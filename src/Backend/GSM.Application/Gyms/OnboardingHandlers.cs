using GSM.Application.Common.Abstractions;
using GSM.Application.Equipment.Public;
using GSM.Application.Gyms.Abstractions;
using GSM.Application.Gyms.Contracts;
using GSM.Application.Gyms.Public;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Gyms;

/// <summary>
/// Calcula el estado del onboarding (Gyms §5). Cada paso está hecho cuando su dato existe de verdad, leído por el
/// <c>I{Módulo}Reader</c> de quien lo guarda. Cuando los cinco obligatorios se cumplen por primera vez, queda
/// constancia en <c>gym_onboarding</c> y desde entonces el gym se da por habilitado aunque después se borre algo.
/// </summary>
/// <remarks>
/// Lee de Equipment, Memberships, Members e Identity, y Equipment/Members ya leen de Gyms: es un ciclo de
/// lecturas entre módulos que la especificación pide explícitamente (§5) y que no escribe en ninguno.
/// </remarks>
public sealed class GetOnboardingHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymRepository gyms,
    IGymReader gymReader,
    IEquipmentReader equipment,
    IMembershipReader memberships,
    IMemberReader members,
    IIdentityReader identity,
    IUnitOfWork unitOfWork)
{
    public const string StepGym = "gym";
    public const string StepLocation = "location";
    public const string StepHours = "hours";
    public const string StepEquipment = "equipment";
    public const string StepPlans = "plans";
    public const string StepEntry = "entry";
    public const string StepTeam = "team";
    public const string TodoMembers = "members";

    private static readonly string[] StepKeys = [StepGym, StepLocation, StepHours, StepEquipment, StepPlans, StepEntry, StepTeam];

    private const int RequiredSteps = 5;

    public Task<OnboardingResponse> HandleAsync(CancellationToken cancellationToken)
        => HandleAsync(currentUser.RequireGymId(), cancellationToken);

    public async Task<OnboardingResponse> HandleAsync(Guid gymId, CancellationToken cancellationToken)
    {
        var gym = await gyms.GetByIdAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym no existe.");
        var onboarding = await gyms.GetOnboardingAsync(gymId, cancellationToken);

        var today = clock.Today;
        var schedule = await gymReader.GetOpeningScheduleAsync(gymId, today, today.AddDays(1), cancellationToken);
        var machines = await equipment.GetActiveItemsAsync(gymId, cancellationToken);
        var planCount = await memberships.GetActivePlanCountAsync(gymId, cancellationToken);
        var team = await identity.GetTeamSetupAsync(gymId, cancellationToken);
        var owners = await identity.ListActiveOwnersAsync(gymId, cancellationToken);
        var memberCount = await members.GetActiveCountAsync(gymId, cancellationToken);

        var done = new[]
        {
            !string.IsNullOrWhiteSpace(gym.Name),
            !string.IsNullOrWhiteSpace(gym.Address) && !string.IsNullOrWhiteSpace(gym.City),
            schedule.HasSchedule,
            machines.Count > 0,
            planCount > 0,
            team.PairedKiosks > 0,
            team.ActiveStaff > 0 || owners.Count > 1,
        };

        var requiredMet = done.Take(RequiredSteps).All(d => d);
        if (requiredMet && (onboarding is null || !onboarding.IsDone))
        {
            if (onboarding is null)
            {
                onboarding = GymOnboarding.Start(gymId);
                gyms.Add(onboarding);
            }

            onboarding.Complete(clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var steps = StepKeys
            .Select((key, i) => new OnboardingStepResponse(key, i + 1, i < RequiredSteps, done[i]))
            .ToList();
        var isComplete = onboarding?.IsDone ?? false;
        var next = isComplete ? null : steps.FirstOrDefault(s => s.Required && !s.Done)?.Key;

        var toComplete = new List<OnboardingTodoResponse>();
        if (!done[5])
        {
            toComplete.Add(new OnboardingTodoResponse(StepEntry));
        }

        if (!done[6])
        {
            toComplete.Add(new OnboardingTodoResponse(StepTeam));
        }

        if (memberCount == 0)
        {
            toComplete.Add(new OnboardingTodoResponse(TodoMembers));
        }

        return new OnboardingResponse(
            isComplete,
            onboarding?.WasSkipped ?? false,
            onboarding?.SkipReason,
            onboarding?.CompletedAt,
            next,
            steps,
            toComplete);
    }
}

/// <summary>
/// El SuperAdmin omite el onboarding de un gym, con motivo (soporte: p. ej. una migración de datos hecha por el
/// equipo). Queda en <c>gym_onboarding</c> (quién, cuándo, por qué) y en el log. 409 si ya estaba completo.
/// </summary>
public sealed class SkipOnboardingHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymRepository gyms,
    IUnitOfWork unitOfWork,
    GetOnboardingHandler reader,
    ILogger<SkipOnboardingHandler> logger)
{
    public async Task<OnboardingResponse> HandleAsync(
        Guid gymId, SkipOnboardingRequest request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario autenticado en el contexto.");

        _ = await gyms.GetByIdAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym no existe.");

        var onboarding = await gyms.GetOnboardingAsync(gymId, cancellationToken);
        if (onboarding is null)
        {
            onboarding = GymOnboarding.Start(gymId);
            gyms.Add(onboarding);
        }

        onboarding.Skip(actorId, request.Reason, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Onboarding del gym {GymId} omitido por {ActorId}", gymId, actorId);
        return await reader.HandleAsync(gymId, cancellationToken);
    }
}
