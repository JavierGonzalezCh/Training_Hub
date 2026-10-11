using GSM.Domain.Notifications;

namespace GSM.Application.Notifications.Abstractions;

/// <summary>Preferencias del miembro por categoría y canal (N4).</summary>
public interface IMemberNotificationPreferenceRepository
{
    /// <summary>Las filas del miembro (con seguimiento, para actualizarlas). Las categorías sin fila usan los valores por defecto.</summary>
    Task<IReadOnlyList<MemberNotificationPreference>> GetForMemberAsync(
        Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>De varios miembros y una categoría: solo las filas que existen (sin seguimiento).</summary>
    Task<IReadOnlyDictionary<Guid, MemberNotificationPreference>> GetForMembersAsync(
        IReadOnlyCollection<Guid> memberIds, NotificationCategory category, CancellationToken cancellationToken = default);

    void Add(MemberNotificationPreference preference);
}

public interface INotificationSettingsRepository
{
    Task<NotificationSettings?> GetAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>Las filas que existen de esos gyms; los que no aparecen usan el horario por defecto. Sin filtro de gym.</summary>
    Task<IReadOnlyDictionary<Guid, NotificationSettings>> GetForGymsAsync(
        IReadOnlyCollection<Guid> gymIds, CancellationToken cancellationToken = default);

    void Add(NotificationSettings settings);
}

/// <summary>
/// Enlace de baja de los correos: token firmado que funciona sin iniciar sesión y apaga el correo de una
/// categoría. Implementado en Infrastructure (HMAC con <c>Communications:UnsubscribeSigningKey</c>).
/// </summary>
public interface IUnsubscribeLinks
{
    string BuildUrl(Guid memberId, Guid gymId, NotificationCategory category);

    /// <param name="category">
    /// La categoría del enlace. Nula en los enlaces anteriores a N4 (que no la traían): apagan el correo de todas.
    /// </param>
    bool TryRead(string? token, out Guid memberId, out Guid gymId, out NotificationCategory? category);
}
