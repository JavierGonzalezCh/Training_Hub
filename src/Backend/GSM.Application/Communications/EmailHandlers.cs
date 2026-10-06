using GSM.Application.Common.Abstractions;
using GSM.Application.Communications.Abstractions;
using GSM.Application.Communications.Contracts;
using GSM.Application.Members.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Communications;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Communications;

public sealed class GetCommunicationPreferencesHandler(
    ICurrentUserService currentUser,
    IMemberReader members,
    IMemberCommunicationPreferencesRepository preferences)
{
    public async Task<CommunicationPreferencesResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var self = await FeedMember.ResolveAsync(currentUser, members, cancellationToken);
        var current = await preferences.GetAsync(self.Id, cancellationToken);
        return new CommunicationPreferencesResponse(current?.EmailOptOut ?? false);
    }
}

public sealed class UpdateCommunicationPreferencesHandler(
    ICurrentUserService currentUser,
    IMemberReader members,
    IMemberCommunicationPreferencesRepository preferences,
    IUnitOfWork unitOfWork,
    ILogger<UpdateCommunicationPreferencesHandler> logger)
{
    public async Task<CommunicationPreferencesResponse> HandleAsync(
        UpdateCommunicationPreferencesRequest request, CancellationToken cancellationToken)
    {
        var self = await FeedMember.ResolveAsync(currentUser, members, cancellationToken);
        await CommunicationPreferencesWriter.SetEmailOptOutAsync(
            preferences, self.GymId, self.Id, request.EmailOptOut, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Miembro {MemberId}: baja de email = {OptOut}", self.Id, request.EmailOptOut);
        return new CommunicationPreferencesResponse(request.EmailOptOut);
    }
}

/// <summary>
/// Baja desde el enlace del correo, sin iniciar sesión. Corre sin usuario (y por tanto sin Global
/// Query Filter): el gym y el miembro salen del token firmado. Repetirlo no tiene efecto: la baja
/// es idempotente, así que el mismo enlace puede pulsarse varias veces sin error.
/// </summary>
public sealed class UnsubscribeHandler(
    IUnsubscribeLinks unsubscribeLinks,
    IMemberReader members,
    IMemberCommunicationPreferencesRepository preferences,
    IUnitOfWork unitOfWork,
    ILogger<UnsubscribeHandler> logger)
{
    public async Task HandleAsync(string? token, CancellationToken cancellationToken)
    {
        if (!unsubscribeLinks.TryRead(token, out var memberId, out var gymId))
        {
            throw new ValidationDomainException("El enlace de baja no es válido.");
        }

        var member = await members.GetContactAsync(memberId, cancellationToken);
        if (member is null || member.GymId != gymId)
        {
            throw new ValidationDomainException("El enlace de baja no es válido.");
        }

        await CommunicationPreferencesWriter.SetEmailOptOutAsync(preferences, gymId, memberId, true, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Miembro {MemberId} se dio de baja del email desde el enlace", memberId);
    }
}

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

/// <summary>Cuántos miembros recibirían un anuncio por email: el editor lo muestra antes de publicar.</summary>
public sealed class GetEmailAudienceHandler(
    ICurrentUserService currentUser,
    IMemberReader members,
    IMemberCommunicationPreferencesRepository preferences)
{
    public async Task<EmailAudienceResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var contacts = await members.GetEmailContactsAsync(gymId, cancellationToken);
        var optedOut = await preferences.GetOptedOutAsync(contacts.Select(c => c.Id).ToList(), cancellationToken);
        return new EmailAudienceResponse(contacts.Count(c => !optedOut.Contains(c.Id)));
    }
}

internal static class CommunicationPreferencesWriter
{
    public static async Task SetEmailOptOutAsync(
        IMemberCommunicationPreferencesRepository preferences,
        Guid gymId,
        Guid memberId,
        bool optOut,
        CancellationToken cancellationToken)
    {
        var current = await preferences.GetAsync(memberId, cancellationToken);
        if (current is null)
        {
            current = MemberCommunicationPreferences.Create(gymId, memberId);
            preferences.Add(current);
        }

        current.SetEmailOptOut(optOut);
    }
}
