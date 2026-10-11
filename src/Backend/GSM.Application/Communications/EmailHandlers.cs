using GSM.Application.Common.Abstractions;
using GSM.Application.Communications.Abstractions;
using GSM.Application.Communications.Contracts;
using GSM.Application.Members.Public;
using GSM.Application.Notifications.Public;
using GSM.Domain.Notifications;
using GSM.Domain.Communications;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Communications;

public sealed class GetCommunicationsSettingsHandler(
    ICurrentUserService currentUser, ICommunicationsSettingsRepository settings)
{
    public async Task<CommunicationsSettingsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var current = await settings.GetAsync(gymId, cancellationToken) ?? CommunicationsSettings.Default(gymId);
        return new CommunicationsSettingsResponse(current.ReminderDaysBefore);
    }
}

public sealed class UpdateCommunicationsSettingsHandler(
    ICurrentUserService currentUser,
    ICommunicationsSettingsRepository settings,
    IUnitOfWork unitOfWork,
    ILogger<UpdateCommunicationsSettingsHandler> logger)
{
    public async Task<CommunicationsSettingsResponse> HandleAsync(
        UpdateCommunicationsSettingsRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var current = await settings.GetAsync(gymId, cancellationToken);
        if (current is null)
        {
            current = CommunicationsSettings.Default(gymId);
            settings.Add(current);
        }

        current.Update(request.ReminderDaysBefore);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Gym {GymId}: recordatorio de vencimiento a {Days} días", gymId, current.ReminderDaysBefore);
        return new CommunicationsSettingsResponse(current.ReminderDaysBefore);
    }
}

/// <summary>
/// Cuántos miembros recibirían un anuncio por email: el editor lo muestra antes de publicar. Respeta la
/// preferencia de correo de la categoría del anuncio (Novedades o Promociones).
/// </summary>
public sealed class GetEmailAudienceHandler(
    ICurrentUserService currentUser,
    IMemberReader members,
    INotificationPreferenceReader preferences)
{
    public async Task<EmailAudienceResponse> HandleAsync(
        AnnouncementCategory category, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var contacts = await members.GetEmailContactsAsync(gymId, cancellationToken);
        var enabled = await preferences.GetEmailEnabledAsync(
            contacts.Select(c => c.Id).ToList(),
            category == AnnouncementCategory.Promotions ? NotificationCategory.Promotions : NotificationCategory.News,
            cancellationToken);
        return new EmailAudienceResponse(contacts.Count(c => enabled.Contains(c.Id)));
    }
}
