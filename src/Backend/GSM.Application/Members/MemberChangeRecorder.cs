using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Abstractions;
using GSM.Domain.Identity;
using GSM.Domain.Members;

namespace GSM.Application.Members;

/// <summary>
/// Escribe las filas del historial de cambios de la ficha (Members v2, §4). Solo las agrega al
/// contexto: se confirman con el mismo <c>SaveChanges</c> del caso de uso, así que no hay cambio
/// sin su fila.
/// </summary>
public sealed class MemberChangeRecorder(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberRepository members)
{
    public void RecordCreated(Member member)
        => Add(member, MemberChange.CreatedField, oldValue: null, newValue: null);

    public void Record(Member member, IEnumerable<MemberFieldChange> changes)
    {
        foreach (var change in changes)
        {
            Add(member, change.Field, change.OldValue, change.NewValue);
        }
    }

    public void RecordStatus(Member member, MemberStatus from, MemberStatus to)
        => Add(member, MemberChange.StatusField, EnumText.Snake(from), EnumText.Snake(to));

    private void Add(Member member, string field, string? oldValue, string? newValue)
    {
        var role = currentUser.Role switch
        {
            UserRole.Owner => MemberChangeRole.Owner,
            UserRole.Staff => MemberChangeRole.Staff,
            UserRole.SuperAdmin => MemberChangeRole.Superadmin,
            UserRole.Member => MemberChangeRole.Member,
            _ => MemberChangeRole.System,
        };

        var source = currentUser.Role switch
        {
            UserRole.Member => MemberChangeSource.App,
            null => MemberChangeSource.System,
            _ => MemberChangeSource.Panel,
        };

        members.Add(MemberChange.Record(
            member.GymId, member.Id, field, oldValue, newValue,
            currentUser.UserId, role, source, clock.UtcNow));
    }
}
