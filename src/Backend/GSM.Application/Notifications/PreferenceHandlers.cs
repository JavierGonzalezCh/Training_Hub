using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Public;
using GSM.Application.Notifications.Abstractions;
using GSM.Application.Notifications.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Notifications;

/// <summary>Las preferencias del miembro por categoría y canal; sin fila, los valores por defecto.</summary>
public sealed class GetMyNotificationPreferencesHandler(
    ICurrentUserService currentUser,
    IMemberReader members,
    IMemberNotificationPreferenceRepository preferences)
{
    public async Task<NotificationPreferencesResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var self = await MemberInbox.ResolveAsync(currentUser, members, cancellationToken);
        return PreferenceMapper.ToResponse(await preferences.GetForMemberAsync(self.Id, cancellationToken));
    }
}

public sealed class UpdateMyNotificationPreferencesHandler(
    ICurrentUserService currentUser,
    IMemberReader members,
    IMemberNotificationPreferenceRepository preferences,
    IUnitOfWork unitOfWork,
    ILogger<UpdateMyNotificationPreferencesHandler> logger)
{
    public async Task<NotificationPreferencesResponse> HandleAsync(
        UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken)
    {
        var self = await MemberInbox.ResolveAsync(currentUser, members, cancellationToken);
        var rows = (await preferences.GetForMemberAsync(self.Id, cancellationToken)).ToList();

        foreach (var item in request.Items)
        {
            var category = PreferenceMapper.ParseCategory(item.Category);
            var row = rows.FirstOrDefault(r => r.Category == category);
            if (row is null)
            {
                row = MemberNotificationPreference.Create(self.GymId, self.Id, category, item.Push, item.Email);
                preferences.Add(row);
                rows.Add(row);
            }
            else
            {
                row.Set(item.Push, item.Email);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Miembro {MemberId}: preferencias de avisos actualizadas", self.Id);
        return PreferenceMapper.ToResponse(rows);
    }
}

/// <summary>
/// Baja desde el enlace del correo, sin iniciar sesión: apaga el correo de <b>esa categoría</b>, no de todas.
/// Corre sin usuario (y por tanto sin Global Query Filter): el gym, el miembro y la categoría salen del
/// token firmado. Es idempotente: el mismo enlace puede pulsarse varias veces sin error. Los enlaces
/// anteriores a N4 no traían categoría y apagan el correo de todas.
/// </summary>
public sealed class UnsubscribeHandler(
    IUnsubscribeLinks unsubscribeLinks,
    IMemberReader members,
    IMemberNotificationPreferenceRepository preferences,
    IUnitOfWork unitOfWork,
    ILogger<UnsubscribeHandler> logger)
{
    /// <returns>El nombre de la categoría dada de baja, o <c>null</c> si fueron todas (enlace anterior a N4).</returns>
    public async Task<string?> HandleAsync(string? token, CancellationToken cancellationToken)
    {
        if (!unsubscribeLinks.TryRead(token, out var memberId, out var gymId, out var category))
        {
            throw new ValidationDomainException("El enlace de baja no es válido.");
        }

        var member = await members.GetContactAsync(memberId, cancellationToken);
        if (member is null || member.GymId != gymId)
        {
            throw new ValidationDomainException("El enlace de baja no es válido.");
        }

        var rows = (await preferences.GetForMemberAsync(memberId, cancellationToken)).ToList();
        List<NotificationCategory> targets = category is { } one ? [one] : [.. MemberNotificationPreference.Configurable];
        foreach (var target in targets)
        {
            if (!MemberNotificationPreference.IsConfigurable(target))
            {
                continue; // los avisos de seguridad no tienen preferencia ni baja
            }

            var row = rows.FirstOrDefault(r => r.Category == target);
            if (row is null)
            {
                var (push, _) = MemberNotificationPreference.Defaults(target);
                preferences.Add(MemberNotificationPreference.Create(gymId, memberId, target, push, email: false));
            }
            else
            {
                row.Set(row.Push, email: false);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Miembro {MemberId} se dio de baja del correo ({Category}) desde el enlace",
            memberId, category?.ToString() ?? "todas");
        return category is { } c ? NotificationCategoryLabels.Es(c) : null;
    }
}

public sealed class GetNotificationSettingsHandler(
    ICurrentUserService currentUser, INotificationSettingsRepository settings)
{
    public async Task<NotificationSettingsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var current = await settings.GetAsync(gymId, cancellationToken) ?? NotificationSettings.Default(gymId);
        return PreferenceMapper.ToResponse(current);
    }
}

public sealed class UpdateNotificationSettingsHandler(
    ICurrentUserService currentUser,
    INotificationSettingsRepository settings,
    IUnitOfWork unitOfWork,
    ILogger<UpdateNotificationSettingsHandler> logger)
{
    public async Task<NotificationSettingsResponse> HandleAsync(
        UpdateNotificationSettingsRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var current = await settings.GetAsync(gymId, cancellationToken);
        if (current is null)
        {
            current = NotificationSettings.Default(gymId);
            settings.Add(current);
        }

        current.Update(request.QuietHoursEnabled, request.QuietStart, request.QuietEnd);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Gym {GymId}: horario sin avisos {Enabled} {Start}-{End}",
            gymId, current.QuietHoursEnabled, current.QuietStart, current.QuietEnd);
        return PreferenceMapper.ToResponse(current);
    }
}

internal static class PreferenceMapper
{
    public static NotificationPreferencesResponse ToResponse(IEnumerable<MemberNotificationPreference> rows)
    {
        var byCategory = rows.ToDictionary(r => r.Category);
        var items = MemberNotificationPreference.Configurable.Select(category =>
        {
            var (push, email) = byCategory.TryGetValue(category, out var row)
                ? (row.Push, row.Email)
                : MemberNotificationPreference.Defaults(category);
            return new NotificationPreferenceItem(
                category.ToString(), NotificationCategoryLabels.Es(category), push, email);
        }).ToList();
        return new NotificationPreferencesResponse(items);
    }

    public static NotificationSettingsResponse ToResponse(NotificationSettings s)
        => new(s.QuietHoursEnabled, s.QuietStart, s.QuietEnd);

    public static NotificationCategory ParseCategory(string value)
        => Enum.TryParse<NotificationCategory>(value, ignoreCase: true, out var category)
            && MemberNotificationPreference.IsConfigurable(category)
                ? category
                : throw new ValidationDomainException($"Categoría de avisos inválida: {value}.");
}
