using GSM.Application.Notifications.Abstractions;
using GSM.Domain.Notifications;

namespace GSM.Application.Notifications.Public;

internal sealed class NotificationPreferenceReader(IMemberNotificationPreferenceRepository preferences)
    : INotificationPreferenceReader
{
    public async Task<IReadOnlySet<Guid>> GetEmailEnabledAsync(
        IReadOnlyCollection<Guid> memberIds, NotificationCategory category, CancellationToken cancellationToken = default)
    {
        var rows = await preferences.GetForMembersAsync(memberIds, category, cancellationToken);
        var byDefault = MemberNotificationPreference.Defaults(category).Email;
        return memberIds.Where(id => rows.TryGetValue(id, out var row) ? row.Email : byDefault).ToHashSet();
    }
}
