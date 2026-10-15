using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Communications.Abstractions;
using GSM.Application.Communications.Contracts;
using GSM.Application.Communications.Events;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Domain.Communications;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Communications;

/// <summary>Constantes y armado del texto de la felicitación de cumpleaños (§F).</summary>
internal static class BirthdayGreeting
{
    public const string Title = "¡Feliz cumpleaños!";

    /// <summary>Hora local del gym a la que sale la felicitación (8:00 a. m., §F).</summary>
    public static readonly TimeOnly SendTime = new(8, 0);

    public static readonly IReadOnlyList<string> Variables = ["{nombre}", "{gym}"];

    public static string Build(string template, string firstName, string gymName)
        => template
            .Replace("{nombre}", firstName, StringComparison.Ordinal)
            .Replace("{gym}", gymName, StringComparison.Ordinal);
}

public sealed class GetBirthdaySettingsHandler(
    ICurrentUserService currentUser, ICommunicationsSettingsRepository settings)
{
    public async Task<BirthdaySettingsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var current = await settings.GetAsync(gymId, cancellationToken) ?? CommunicationsSettings.Default(gymId);
        return BirthdaySettingsMapper.ToResponse(current);
    }
}

public sealed class UpdateBirthdaySettingsHandler(
    ICurrentUserService currentUser,
    ICommunicationsSettingsRepository settings,
    IUnitOfWork unitOfWork,
    ILogger<UpdateBirthdaySettingsHandler> logger)
{
    public async Task<BirthdaySettingsResponse> HandleAsync(
        UpdateBirthdaySettingsRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var current = await settings.GetAsync(gymId, cancellationToken);
        if (current is null)
        {
            current = CommunicationsSettings.Default(gymId);
            settings.Add(current);
        }

        current.UpdateBirthday(request.Enabled, request.Message);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Gym {GymId}: felicitación de cumpleaños {State}",
            gymId, current.BirthdayEnabled ? "encendida" : "apagada");
        return BirthdaySettingsMapper.ToResponse(current);
    }
}

internal static class BirthdaySettingsMapper
{
    public static BirthdaySettingsResponse ToResponse(CommunicationsSettings settings)
        => new(
            settings.BirthdayEnabled,
            settings.EffectiveBirthdayMessage,
            CommunicationsSettings.DefaultBirthdayMessage,
            settings.BirthdayMessage is null,
            BirthdayGreeting.Variables);
}

/// <summary>
/// Decide a quién felicitar hoy y lo publica como <see cref="BirthdayGreetingDue"/> (Notifications lo programa
/// para las 8:00 y deduplica por miembro y día). Lo invoca la tarea periódica, así que corre <b>sin usuario</b>:
/// lleva <c>gym_id</c> explícito en todo. Solo felicita a miembros activos de gyms que lo encendieron.
/// <para>
/// Solo cubre los cumpleaños de <b>hoy</b>: si la tarea no corre un día, ese día no se felicita (una felicitación
/// tardía no tiene sentido). Si corre más de una vez, el evento se repite y Notifications no duplica el aviso.
/// </para>
/// </summary>
public sealed class PublishBirthdayGreetingsHandler(
    IClock clock,
    IMemberReader members,
    IGymReader gyms,
    ICommunicationsSettingsRepository settings,
    IDomainEventPublisher events,
    ILogger<PublishBirthdayGreetingsHandler> logger)
{
    /// <returns>Cuántas felicitaciones se publicaron.</returns>
    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var eightOClock = clock.BusinessDayRange(today).Start + BirthdayGreeting.SendTime.ToTimeSpan();
        // Nunca en el pasado: si la tarea corre después de las 8:00, sale ya.
        var sendAt = clock.UtcNow < eightOClock ? eightOClock : clock.UtcNow;

        var published = 0;
        foreach (var gymSettings in await settings.ListBirthdayEnabledAsync(cancellationToken))
        {
            var gymId = gymSettings.GymId;
            var birthdays = await members.GetBirthdaysOnAsync(gymId, today, cancellationToken);
            if (birthdays.Count == 0)
            {
                continue;
            }

            var gymName = (await gyms.GetBrandingAsync(gymId, cancellationToken))?.Name ?? string.Empty;
            var template = gymSettings.EffectiveBirthdayMessage;
            foreach (var member in birthdays)
            {
                await events.PublishAsync(
                    new BirthdayGreetingDue(
                        gymId, member.Id, today, BirthdayGreeting.Title,
                        BirthdayGreeting.Build(template, member.FirstName, gymName), sendAt),
                    cancellationToken);
                published++;
            }
        }

        logger.LogInformation("Cumpleaños {Date}: {Count} felicitaciones publicadas", today, published);
        return published;
    }
}
